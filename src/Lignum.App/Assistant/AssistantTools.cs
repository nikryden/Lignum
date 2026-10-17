using System.Text;
using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using Lignum.App.ViewModels;
using Lignum.Core;

namespace Lignum.App.Assistant;

/// <summary>The outcome of one tool call: text for Claude, and whether it failed.</summary>
public sealed record ToolOutcome(string Content, bool IsError = false);

/// <summary>
/// The tools Claude may call (ticket 05). They act on the open tabs through the view models, so every change
/// shows in the UI and can be undone there; nothing ever writes to a log file. Reading tools are capped, and
/// each read is reported through <see cref="LinesRead"/> so the user sees what was sent.
/// </summary>
public sealed class AssistantTools(MainViewModel main)
{
    internal const int MaxSearchLines = 200;
    internal const int MaxReadLines = 200;
    private const int MaxLineChars = 2000;

    /// <summary>Raised after a tool read log lines: (file name, number of lines).</summary>
    public event Action<string, int>? LinesRead;

    public static IReadOnlyList<BetaToolUnion> Definitions { get; } =
    [
        Define("list_tabs", "List the log files open in Lignum: their tab index, file name, line count and which tab is selected.",
            new()),
        Define("search",
            "Search the selected tab's log file. Returns the number of matching lines and the first matches with their 1-based line numbers. Patterns are .NET regular expressions when is_regex is true.",
            new()
            {
                ["pattern"] = Prop("string", "Text or .NET regex to find."),
                ["is_regex"] = Prop("boolean", "Treat pattern as a regular expression (default false)."),
                ["match_case"] = Prop("boolean", "Case-sensitive (default false)."),
                ["max_lines"] = Prop("integer", $"How many matching lines to return (default 50, at most {MaxSearchLines})."),
            }, "pattern"),
        Define("read_lines", $"Read consecutive lines of the selected tab's log file, starting at a 1-based line number (at most {MaxReadLines} lines; long lines are cut).",
            new()
            {
                ["first_line"] = Prop("integer", "1-based line number to start at."),
                ["count"] = Prop("integer", $"How many lines (at most {MaxReadLines})."),
            }, "first_line", "count"),
        Define("add_filter_rule", "Add a filter rule to the selected tab: include rules keep only matching lines, exclude rules hide them. The results pane updates.",
            new()
            {
                ["text"] = Prop("string", "Text or .NET regex."),
                ["kind"] = Prop("string", "\"include\" or \"exclude\"."),
                ["is_regex"] = Prop("boolean", "Treat text as a regular expression (default false)."),
                ["match_case"] = Prop("boolean", "Case-sensitive (default false)."),
            }, "text", "kind"),
        Define("add_highlight_rule", "Add a line-coloring rule (applies to every tab). Lines that match get the default highlight colors; the user can change them later.",
            new()
            {
                ["pattern"] = Prop("string", "Text or .NET regex."),
                ["is_regex"] = Prop("boolean", "Treat pattern as a regular expression (default false)."),
                ["match_case"] = Prop("boolean", "Case-sensitive (default false)."),
            }, "pattern"),
        Define("add_bookmark", "Bookmark a line of the selected tab, optionally with a short note.",
            new()
            {
                ["line"] = Prop("integer", "1-based line number."),
                ["note"] = Prop("string", "Optional note shown on the bookmark."),
            }, "line"),
        Define("go_to_line", "Scroll the selected tab to a 1-based line and put the caret there.",
            new() { ["line"] = Prop("integer", "1-based line number.") }, "line"),
    ];

    /// <summary>Runs one tool call. Invalid input comes back as an error result, never as an exception.</summary>
    public async Task<ToolOutcome> RunAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            return call.Name switch
            {
                "list_tabs" => ListTabs(),
                "search" => await SearchAsync(call.Input, cancellationToken),
                "read_lines" => ReadLines(call.Input),
                "add_filter_rule" => AddFilterRule(call.Input),
                "add_highlight_rule" => AddHighlightRule(call.Input),
                "add_bookmark" => AddBookmark(call.Input),
                "go_to_line" => GoToLine(call.Input),
                _ => new ToolOutcome($"Unknown tool {call.Name}.", IsError: true),
            };
        }
        catch (Exception ex) when (ex is ToolInputException or ArgumentException or InvalidOperationException
                                       or KeyNotFoundException or IOException)
        {
            return new ToolOutcome(ex.Message, IsError: true);
        }
    }

    private ToolOutcome ListTabs()
    {
        if (main.Tabs.Count == 0) return new ToolOutcome("No log files are open.");
        var text = new StringBuilder();
        for (var i = 0; i < main.Tabs.Count; i++)
        {
            var tab = main.Tabs[i];
            text.AppendLine($"{i}: {tab.FileName} ({tab.LineCount:N0} lines){(ReferenceEquals(tab, main.SelectedTab) ? " [selected]" : "")}");
        }

        return new ToolOutcome(text.ToString());
    }

    private async Task<ToolOutcome> SearchAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken token)
    {
        var (tab, document) = Selected();
        var pattern = String(input, "pattern");
        var max = Math.Clamp(Int(input, "max_lines", 50), 1, MaxSearchLines);
        var query = Query.Find(pattern, Bool(input, "match_case"), Bool(input, "is_regex"));
        using var result = document.StartQuery(query);
        using (token.Register(result.Cancel)) await result.Completion;
        token.ThrowIfCancellationRequested();
        if (result.State == QueryState.Failed) return new ToolOutcome($"Search failed: {result.Error?.Message}", IsError: true);

        var shown = (int)Math.Min(result.Count, max);
        var text = new StringBuilder($"{result.Count:N0} matching lines in {tab.FileName}");
        text.AppendLine(shown < result.Count ? $"; the first {shown}:" : ":");
        var lines = await Task.Run(() =>
        {
            var found = new List<(long Line, string Text)>(shown);
            for (var i = 0; i < shown; i++)
            {
                var line = result[i];
                found.Add((line, Prefix(document, line)));
            }

            return found;
        }, token);
        foreach (var (line, lineText) in lines) text.AppendLine($"{line + 1}: {lineText}");
        LinesRead?.Invoke(tab.FileName, shown);
        return new ToolOutcome(text.ToString());
    }

    private ToolOutcome ReadLines(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, document) = Selected();
        var first = Int(input, "first_line", 1) - 1;
        var count = Math.Clamp(Int(input, "count", 50), 1, MaxReadLines);
        if (first < 0 || first >= tab.LineCount)
            return new ToolOutcome($"Line {first + 1} is outside the file (1 - {tab.LineCount:N0}).", IsError: true);
        count = (int)Math.Min(count, tab.LineCount - first);
        var prefixes = document.ReadLinePrefixes(first, count, MaxLineChars);
        var text = new StringBuilder();
        for (var i = 0; i < prefixes.Length; i++)
            text.AppendLine($"{first + i + 1}: {prefixes[i].Text}{(prefixes[i].IsTruncated ? " …" : "")}");
        LinesRead?.Invoke(tab.FileName, prefixes.Length);
        return new ToolOutcome(text.ToString());
    }

    private ToolOutcome AddFilterRule(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected();
        var kind = String(input, "kind").ToLowerInvariant() switch
        {
            "include" => RuleKind.Include,
            "exclude" => RuleKind.Exclude,
            var other => throw new ToolInputException($"kind must be \"include\" or \"exclude\", not \"{other}\"."),
        };
        var rule = new QueryRule(String(input, "text"), kind, Bool(input, "is_regex"), Bool(input, "match_case"));
        _ = new Query([rule]); // an invalid regex throws ArgumentException with a readable message
        tab.Search.AddFilterRule(rule);
        tab.Search.IsResultsPaneOpen = true;
        return new ToolOutcome($"Added {kind.ToString().ToLowerInvariant()} rule \"{rule.Text}\" to {tab.FileName}.");
    }

    private ToolOutcome AddHighlightRule(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (main.Highlighting is not { } highlighting) return new ToolOutcome("Highlighting is not available.", IsError: true);
        var pattern = String(input, "pattern");
        var isRegex = Bool(input, "is_regex");
        if (isRegex) _ = new Query([new QueryRule(pattern, IsRegex: true)]);
        highlighting.AddRule(pattern, isRegex, Bool(input, "match_case"));
        return new ToolOutcome($"Added highlight rule \"{pattern}\".");
    }

    private ToolOutcome AddBookmark(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected();
        var line = LineIn(tab, Int(input, "line", 0));
        var note = input.TryGetValue("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (!string.IsNullOrWhiteSpace(note)) tab.Bookmarks.SetNote(line, note);
        else if (!tab.Bookmarks.Lines.Contains(line)) tab.Bookmarks.Toggle(line);
        return new ToolOutcome($"Bookmarked line {line + 1} of {tab.FileName}.");
    }

    private ToolOutcome GoToLine(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected();
        var line = LineIn(tab, Int(input, "line", 0));
        tab.Navigation.GoToLine(line);
        return new ToolOutcome($"Moved to line {line + 1}.");
    }

    private (LogTabViewModel Tab, LogDocument Document) Selected() =>
        main.SelectedTab is { Document: { } document } tab
            ? (tab, document)
            : throw new ToolInputException("No log file is open in Lignum.");

    private static long LineIn(LogTabViewModel tab, int oneBased) =>
        oneBased >= 1 && oneBased <= tab.LineCount
            ? oneBased - 1
            : throw new ToolInputException($"Line {oneBased} is outside the file (1 - {tab.LineCount:N0}).");

    private static string Prefix(LogDocument document, long line)
    {
        var prefix = document.ReadLinePrefixes(line, 1, MaxLineChars);
        return prefix.Length == 0 ? "" : prefix[0].Text + (prefix[0].IsTruncated ? " …" : "");
    }

    private static string String(IReadOnlyDictionary<string, JsonElement> input, string name) =>
        input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s
            ? s
            : throw new ToolInputException($"\"{name}\" is required and must be a non-empty string.");

    private static bool Bool(IReadOnlyDictionary<string, JsonElement> input, string name) =>
        input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int Int(IReadOnlyDictionary<string, JsonElement> input, string name, int fallback) =>
        !input.TryGetValue(name, out var value) ? fallback
        : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i) ? i
        : throw new ToolInputException($"\"{name}\" must be a whole number.");

    private static BetaToolUnion Define(string name, string description, Dictionary<string, JsonElement> properties,
                                        params string[] required) =>
        new BetaTool
        {
            Name = name,
            Description = description,
            InputSchema = new() { Properties = properties, Required = required },
            EagerInputStreaming = true, // the input is validated above, so a cut-off input becomes an error result
        };

    private static JsonElement Prop(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });
}

/// <summary>A tool input that does not match the tool's schema; sent back to Claude as an error result.</summary>
public sealed class ToolInputException(string message) : Exception(message);
