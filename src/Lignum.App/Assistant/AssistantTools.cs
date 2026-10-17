using System.Text;
using System.Text.Json;
using Lignum.App.ViewModels;
using Lignum.Core;

namespace Lignum.App.Assistant;

/// <summary>One tool call from an AI agent: the tool name and its JSON arguments.</summary>
public sealed record ToolCall(string Name, IReadOnlyDictionary<string, JsonElement> Input);

/// <summary>The outcome of one tool call: text for the agent, and whether it failed.</summary>
public sealed record ToolOutcome(string Content, bool IsError = false);

/// <summary>
/// The tools an AI agent may call through AI access (ticket 05). They act on the open tabs through the view models, so every change
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
                "select_tab" => SelectTab(call.Input),
                "search_all_tabs" => await SearchAllTabsAsync(call.Input, cancellationToken),
                "find_in_files" => await FindInFilesAsync(call.Input, cancellationToken),
                "show_search" => await ShowSearchAsync(call.Input),
                "list_filter_rules" => ListFilterRules(call.Input),
                "remove_filter_rule" => RemoveFilterRule(call.Input),
                "set_filter_rule_enabled" => SetFilterRuleEnabled(call.Input),
                "clear_filter_rules" => ClearFilterRules(call.Input),
                "list_bookmarks" => ListBookmarks(call.Input),
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
        var (tab, document) = Selected(input);
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
        var (tab, document) = Selected(input);
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
        var (tab, _) = Selected(input);
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
        var (tab, _) = Selected(input);
        var line = LineIn(tab, Int(input, "line", 0));
        var note = input.TryGetValue("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (!string.IsNullOrWhiteSpace(note)) tab.Bookmarks.SetNote(line, note);
        else if (!tab.Bookmarks.Lines.Contains(line)) tab.Bookmarks.Toggle(line);
        return new ToolOutcome($"Bookmarked line {line + 1} of {tab.FileName}.");
    }

    private ToolOutcome GoToLine(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var line = LineIn(tab, Int(input, "line", 0));
        tab.Navigation.GoToLine(line);
        return new ToolOutcome($"Moved to line {line + 1}.");
    }

    /// <summary>The tab named by the optional "tab" input (index from list_tabs, or file name), else the selected one.</summary>
    private (LogTabViewModel Tab, LogDocument Document) Selected(IReadOnlyDictionary<string, JsonElement> input)
    {
        var tab = main.SelectedTab;
        if (input.TryGetValue("tab", out var which) && which.ValueKind is JsonValueKind.Number or JsonValueKind.String)
        {
            var key = which.ValueKind == JsonValueKind.Number ? which.GetRawText() : which.GetString() ?? "";
            tab = int.TryParse(key, out var index) && index >= 0 && index < main.Tabs.Count
                ? main.Tabs[index]
                : main.Tabs.FirstOrDefault(t => string.Equals(t.FileName, key, StringComparison.OrdinalIgnoreCase))
                  ?? throw new ToolInputException($"No open tab \"{key}\". Use list_tabs to see them.");
        }

        return tab is { Document: { } document }
            ? (tab, document)
            : throw new ToolInputException("No log file is open in Lignum.");
    }

    private ToolOutcome SelectTab(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        main.SelectedTab = tab;
        return new ToolOutcome($"Selected {tab.FileName}.");
    }

    private async Task<ToolOutcome> SearchAllTabsAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken token)
    {
        var query = Query.Find(String(input, "pattern"), Bool(input, "match_case"), Bool(input, "is_regex"));
        var max = Math.Clamp(Int(input, "max_lines_per_tab", 20), 0, 100);
        var text = new StringBuilder();
        var tabs = main.Tabs.Where(t => t.Document is not null).ToList();
        if (tabs.Count == 0) return new ToolOutcome("No log files are open.");
        for (var i = 0; i < tabs.Count; i++)
        {
            var (count, lines) = await MatchesAsync(tabs[i].Document!, query, max, token);
            text.AppendLine($"[{main.Tabs.IndexOf(tabs[i])}] {tabs[i].FileName}: {count:N0} matching lines");
            foreach (var (line, lineText) in lines) text.AppendLine($"  {line + 1}: {lineText}");
            if (lines.Count > 0) LinesRead?.Invoke(tabs[i].FileName, lines.Count);
        }

        return new ToolOutcome(text.ToString());
    }

    private async Task<ToolOutcome> FindInFilesAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken token)
    {
        var folder = String(input, "folder");
        if (!Directory.Exists(folder)) return new ToolOutcome($"Folder not found: {folder}", IsError: true);
        var query = Query.Find(String(input, "pattern"), Bool(input, "match_case"), Bool(input, "is_regex"));
        var patterns = input.TryGetValue("file_patterns", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "*.log;*.txt";
        var subfolders = !input.TryGetValue("include_subfolders", out var sub) || sub.ValueKind != JsonValueKind.False;
        var max = Math.Clamp(Int(input, "max_lines_per_file", 10), 0, 50);
        var maxFiles = Math.Clamp(Int(input, "max_files", 200), 1, 1000);
        var (files, _) = await Task.Run(() => Lignum.Core.Searching.FileFinder.Find(folder, subfolders, patterns, token), token);
        var text = new StringBuilder();
        int searched = 0, withMatches = 0, binary = 0;
        foreach (var file in files.Take(maxFiles))
        {
            token.ThrowIfCancellationRequested();
            if (Lignum.Core.Searching.FileFinder.LooksBinary(file))
            {
                binary++;
                continue;
            }

            searched++;
            using var document = LogDocument.Open(file);
            var indexing = document.StartIndexing();
            var (count, lines) = await MatchesAsync(document, query, max, token);
            await indexing;
            if (count == 0) continue;
            withMatches++;
            text.AppendLine($"{file}: {count:N0} matching lines");
            foreach (var (line, lineText) in lines) text.AppendLine($"  {line + 1}: {lineText}");
            if (lines.Count > 0) LinesRead?.Invoke(Path.GetFileName(file), lines.Count);
        }

        text.Insert(0, $"{withMatches} of {searched} files match" + (binary > 0 ? $" ({binary} binary skipped)" : "")
                       + (files.Count > maxFiles ? $"; only the first {maxFiles} of {files.Count} files were searched" : "") + ".\n");
        return new ToolOutcome(text.ToString());
    }

    /// <summary>Runs <paramref name="query"/> over a whole document; the match count and the first lines.</summary>
    private static async Task<(long Count, List<(long Line, string Text)> Lines)> MatchesAsync(
        LogDocument document, Query query, int max, CancellationToken token)
    {
        using var result = document.StartQuery(query);
        using (token.Register(result.Cancel)) await result.Completion;
        token.ThrowIfCancellationRequested();
        if (result.State == QueryState.Failed) throw new ToolInputException($"Search failed: {result.Error?.Message}");
        var shown = (int)Math.Min(result.Count, max);
        var lines = await Task.Run(() =>
        {
            var found = new List<(long, string)>(shown);
            for (var i = 0; i < shown; i++) found.Add((result[i], Prefix(document, result[i])));
            return found;
        }, token);
        return (result.Count, lines);
    }

    private async Task<ToolOutcome> ShowSearchAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var search = tab.Search;
        search.IsRegex = Bool(input, "is_regex");
        search.MatchCase = Bool(input, "match_case");
        search.SearchInsideFilter = !input.TryGetValue("inside_filter", out var inside) || inside.ValueKind != JsonValueKind.False;
        search.FindText = input.TryGetValue("pattern", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
        search.IsResultsPaneOpen = true;
        main.SelectedTab = tab;
        await search.FindAllCommand.ExecuteAsync(null);
        if (search.FindTextError is { } error) return new ToolOutcome(error, IsError: true);
        return new ToolOutcome(search.FindText.Length == 0
            ? $"Cleared the search in {tab.FileName}; the results pane shows the filter's lines."
            : $"Searching in {tab.FileName}'s results pane: {search.ResultsHeader}");
    }

    private ToolOutcome ListFilterRules(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var rules = tab.Search.GetFilterRules();
        if (rules.Count == 0) return new ToolOutcome($"{tab.FileName} has no filter rules.");
        var text = new StringBuilder($"Filter rules of {tab.FileName} (a line is shown when it matches an enabled include rule, if any, and no enabled exclude rule):\n");
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            text.AppendLine($"{i + 1}. {r.Kind.ToString().ToLowerInvariant()} \"{r.Text}\"{(r.IsRegex ? " regex" : "")}{(r.MatchCase ? " match-case" : "")}{(r.IsEnabled ? "" : " (off)")}");
        }

        return new ToolOutcome(text.ToString());
    }

    private ToolOutcome RemoveFilterRule(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var rule = Rule(tab, input);
        tab.Search.FilterRules.Remove(rule);
        return new ToolOutcome($"Removed the filter rule \"{rule.Text}\" from {tab.FileName}.");
    }

    private ToolOutcome SetFilterRuleEnabled(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var rule = Rule(tab, input);
        rule.IsEnabled = input.TryGetValue("enabled", out var on) ? on.ValueKind == JsonValueKind.True
            : throw new ToolInputException("\"enabled\" is required (true or false).");
        return new ToolOutcome($"Turned the filter rule \"{rule.Text}\" {(rule.IsEnabled ? "on" : "off")}.");
    }

    private ToolOutcome ClearFilterRules(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        tab.Search.SetFilterRules([]);
        return new ToolOutcome($"Removed every filter rule from {tab.FileName}.");
    }

    private ToolOutcome ListBookmarks(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var items = tab.Bookmarks.Items;
        if (items.Count == 0) return new ToolOutcome($"{tab.FileName} has no bookmarks.");
        var text = new StringBuilder($"Bookmarks of {tab.FileName}:\n");
        foreach (var item in items) text.AppendLine($"line {item.DisplayLine}{(item.Note is { } note ? $": {note}" : "")}");
        return new ToolOutcome(text.ToString());
    }

    /// <summary>The filter rule at the 1-based "index" from list_filter_rules.</summary>
    private static FilterRuleViewModel Rule(LogTabViewModel tab, IReadOnlyDictionary<string, JsonElement> input)
    {
        var index = Int(input, "index", 0);
        return index >= 1 && index <= tab.Search.FilterRules.Count
            ? tab.Search.FilterRules[index - 1]
            : throw new ToolInputException($"There is no filter rule {index}; use list_filter_rules (1 - {tab.Search.FilterRules.Count}).");
    }

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
}

/// <summary>A tool input that does not match the tool's schema; sent back to Claude as an error result.</summary>
public sealed class ToolInputException(string message) : Exception(message);
