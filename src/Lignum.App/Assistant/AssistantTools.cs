using System.Text;
using System.Text.Json;
using Lignum.App.Assistant.Permissions;
using Lignum.App.Packaging;
using Lignum.App.Settings;
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
public sealed partial class AssistantTools(MainViewModel main)
{
    internal const int MaxSearchLines = 200;
    internal const int MaxReadLines = 200;
    private const int MaxLineChars = 2000;

    /// <summary>The global skills folder (tests use a temp folder).</summary>
    public string GlobalSkillsFolder { get; set; } = SkillLibrary.DefaultGlobalFolder;

    /// <summary>The local skills folder of the selected tab's log (or package), or null without one.</summary>
    public string? LocalSkillsFolder => SkillLibrary.LocalFolderFor(main.SelectedTab?.SourcePath);

    /// <summary>The skills for the selected tab: global ones, and local ones next to its log.</summary>
    public IReadOnlyList<Skill> LoadSkills() => SkillLibrary.Load(GlobalSkillsFolder, LocalSkillsFolder, builtIn: true);

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
                "select_text" => SelectText(call.Input),
                "select_tab" => SelectTab(call.Input),
                "search_all_tabs" => await SearchAllTabsAsync(call.Input, cancellationToken),
                "find_in_files" => await FindInFilesAsync(call.Input, cancellationToken),
                "show_search" => await ShowSearchAsync(call.Input),
                "list_filter_rules" => ListFilterRules(call.Input),
                "remove_filter_rule" => RemoveFilterRule(call.Input),
                "set_filter_rule_enabled" => SetFilterRuleEnabled(call.Input),
                "clear_filter_rules" => ClearFilterRules(call.Input),
                "list_bookmarks" => ListBookmarks(call.Input),
                "remove_bookmark" => RemoveBookmark(call.Input),
                "edit_bookmark" => EditBookmark(call.Input),
                "get_selection" => GetSelection(call.Input),
                "set_hide_non_matching" => SetHideNonMatching(call.Input),
                "list_highlight_rules" => ListHighlightRules(),
                "remove_highlight_rule" => RemoveHighlightRule(call.Input),
                "clear_highlight_rules" => ClearHighlightRules(),
                "show_full_line" => ShowFullLine(call.Input),
                "open_file" => await OpenFileAsync(call.Input, cancellationToken),
                "close_tab" => CloseTab(call.Input),
                "export_package" => await ExportPackageAsync(call.Input),
                "list_skills" => ListSkills(),
                "get_skill" => GetSkill(call.Input),
                "save_skill" => SaveSkill(call.Input),
                "list_dir" => await ListDirAsync(call.Input, cancellationToken),
                "read_file" => await ReadFileAsync(call.Input, cancellationToken),
                "write_file" => await WriteFileAsync(call.Input, cancellationToken),
                "edit_file" => await EditFileAsync(call.Input, cancellationToken),
                "run_command" => await RunCommandAsync(call.Input, cancellationToken),
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
        var color = Color(input);

        if (!string.IsNullOrWhiteSpace(note)) tab.Bookmarks.SetNote(line, note);
        else if (!tab.Bookmarks.Lines.Contains(line)) tab.Bookmarks.Toggle(line);
        if (color is { } chosen) tab.Bookmarks.SetColor(line, chosen);
        return new ToolOutcome($"Bookmarked line {line + 1} of {tab.FileName} ({tab.Bookmarks.ColorFor(line)}).");
    }

    /// <summary>The optional "color" input, or null when it is not given.</summary>
    private static BookmarkColor? Color(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (!input.TryGetValue("color", out var c) || c.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(c.GetString()))
            return null;
        return Enum.TryParse<BookmarkColor>(c.GetString(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ToolInputException($"Unknown bookmark color \"{c.GetString()}\"; use one of {string.Join(", ", Enum.GetNames<BookmarkColor>())}.");
    }

    /// <summary>Removes the bookmark on "line" (with its note and color), or every bookmark with "all".</summary>
    private ToolOutcome RemoveBookmark(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        if (Bool(input, "all"))
        {
            var lines = tab.Bookmarks.Lines;
            foreach (var bookmarked in lines) tab.Bookmarks.Toggle(bookmarked);
            return new ToolOutcome($"Removed {lines.Count:N0} bookmarks from {tab.FileName}.");
        }

        var line = Int(input, "line", 0) - 1;
        if (!tab.Bookmarks.Lines.Contains(line))
            throw new ToolInputException($"Line {line + 1} has no bookmark; use list_bookmarks to see them.");
        tab.Bookmarks.Toggle(line);
        return new ToolOutcome($"Removed the bookmark on line {line + 1} of {tab.FileName}.");
    }

    /// <summary>Changes the note ("" removes it) and / or color of an existing bookmark.</summary>
    private ToolOutcome EditBookmark(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var line = Int(input, "line", 0) - 1;
        if (!tab.Bookmarks.Lines.Contains(line))
            throw new ToolInputException($"Line {line + 1} has no bookmark; use add_bookmark first.");
        var color = Color(input);
        var hasNote = input.TryGetValue("note", out var n) && n.ValueKind == JsonValueKind.String;
        if (!hasNote && color is null) throw new ToolInputException("Give a \"note\" (empty removes it) and / or a \"color\".");
        if (hasNote) tab.Bookmarks.SetNote(line, n.GetString());
        if (color is { } chosen) tab.Bookmarks.SetColor(line, chosen);
        var note = tab.Bookmarks.NoteFor(line);
        return new ToolOutcome($"Bookmark on line {line + 1}: {tab.Bookmarks.ColorFor(line)}{(note is null ? ", no note" : $", note \"{note}\"")}.");
    }

    /// <summary>The caret and the user's selection in the tab's main view, with the selected text (capped).</summary>
    private ToolOutcome GetSelection(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, document) = Selected(input);
        var selection = tab.Selection;
        var caret = tab.Navigation.Caret;
        var text = new StringBuilder($"{tab.FileName}: caret at line {caret.Line + 1}, column {caret.Column + 1}.\n");
        if (selection.IsEmpty)
        {
            text.Append("Nothing is selected.");
            return new ToolOutcome(text.ToString());
        }

        var (start, end) = (selection.Start, selection.End);
        var last = end.Column == 0 && end.Line > start.Line ? end.Line - 1 : end.Line;
        var count = (int)Math.Min(last - start.Line + 1, MaxReadLines);
        text.AppendLine($"Selected: line {start.Line + 1}, column {start.Column + 1} to line {last + 1}" +
                        (last == end.Line && end.Column != int.MaxValue ? $", column {end.Column}." : " (end of line)."));
        if (tab.Search.Results is not null && tab.HideNonMatchingLines)
            text.AppendLine("The main view hides non-matching lines; the lines below include any hidden ones in the range.");
        var lines = document.ReadLinePrefixes(start.Line, count, MaxLineChars);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = start.Line + i;
            var value = lines[i].Text;
            if (line == end.Line && end.Column < value.Length) value = value[..end.Column];
            if (line == start.Line) value = start.Column < value.Length ? value[start.Column..] : "";
            text.AppendLine($"{line + 1}: {value}{(lines[i].IsTruncated ? " …" : "")}");
        }

        if (last - start.Line + 1 > count) text.AppendLine($"(first {count} of {last - start.Line + 1:N0} lines)");
        LinesRead?.Invoke(tab.FileName, lines.Length);
        return new ToolOutcome(text.ToString());
    }

    /// <summary>Turns "Hide non-matching lines" (the main view shows only the filter / search results) on or off.</summary>
    private ToolOutcome SetHideNonMatching(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        tab.HideNonMatchingLines = Bool(input, "enabled");
        return new ToolOutcome(!tab.HideNonMatchingLines
            ? $"{tab.FileName} shows every line."
            : tab.Search.Results is null
                ? $"Hiding non-matching lines in {tab.FileName}, but there is no filter or search yet, so every line still shows."
                : $"{tab.FileName} now shows only the matching lines.");
    }

    private ToolOutcome ListHighlightRules()
    {
        if (main.Highlighting is not { } highlighting) return new ToolOutcome("Highlighting is not available.", IsError: true);
        var rules = highlighting.Rules;
        if (rules.Count == 0) return new ToolOutcome("There are no highlight rules.");
        var text = new StringBuilder($"Highlight rules (preset \"{highlighting.ActivePreset?.Name}\", first match wins):\n");
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            text.AppendLine($"{i + 1}. \"{r.Pattern}\"{(r.IsRegex ? " regex" : "")}{(r.MatchCase ? " match-case" : "")}{(r.IsEnabled ? "" : " (off)")}");
        }

        return new ToolOutcome(text.ToString());
    }

    /// <summary>Removes the highlight rule at the 1-based "index" from list_highlight_rules.</summary>
    private ToolOutcome RemoveHighlightRule(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (main.Highlighting is not { } highlighting) return new ToolOutcome("Highlighting is not available.", IsError: true);
        var index = Int(input, "index", 0);
        if (index < 1 || index > highlighting.Rules.Count)
            throw new ToolInputException($"There is no highlight rule {index}; use list_highlight_rules (1 - {highlighting.Rules.Count}).");
        var rule = highlighting.Rules[index - 1];
        highlighting.Rules.RemoveAt(index - 1);
        return new ToolOutcome($"Removed highlight rule \"{rule.Pattern}\".");
    }

    private ToolOutcome ClearHighlightRules()
    {
        if (main.Highlighting is not { } highlighting) return new ToolOutcome("Highlighting is not available.", IsError: true);
        var count = highlighting.Rules.Count;
        highlighting.Rules.Clear();
        return new ToolOutcome($"Removed {count} highlight rules.");
    }

    /// <summary>Opens the detail pane on a line, for lines too long to read in the view.</summary>
    private ToolOutcome ShowFullLine(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var line = LineIn(tab, Int(input, "line", 0));
        main.SelectedTab = tab;
        tab.Navigation.GoToLine(line);
        main.ShowFullLine(line);
        return new ToolOutcome($"Showing line {line + 1} in the detail pane.");
    }

    /// <summary>Opens a log file or .lm package in a new tab (or selects its tab); returns once the tab is there.</summary>
    private async Task<ToolOutcome> OpenFileAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken)
    {
        var path = String(input, "path");
        if (!File.Exists(path)) throw new ToolInputException($"There is no file \"{path}\".");
        // A file already open in a tab is readable anyway; any other needs a read grant (ADR 0002).
        var alreadyOpen = main.Tabs.Any(t => string.Equals(t.SourcePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (!alreadyOpen && !await AllowedAsync(new PermissionRequest(AiAccess.Read, Path.GetFullPath(path)), cancellationToken))
            return Denied($"opening {path}");
        main.ErrorMessage = null;
        await main.OpenFileCoreAsync(path); // indexing goes on in the background
        var full = Path.GetFullPath(path);
        return main.SelectedTab is { } tab && string.Equals(tab.SourcePath, full, StringComparison.OrdinalIgnoreCase)
            ? new ToolOutcome($"Opened {tab.FileName} as tab {main.Tabs.IndexOf(tab)} (it may still be indexing).")
            : new ToolOutcome(main.ErrorMessage ?? $"Could not open {path}.", IsError: true);
    }

    private ToolOutcome CloseTab(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var name = tab.FileName;
        main.CloseTabCommand.Execute(tab);
        return new ToolOutcome($"Closed {name}.");
    }

    /// <summary>
    /// Exports the tab's log with its bookmarks and notes to a new .lm package at "path". Never overwrites a file.
    /// </summary>
    private async Task<ToolOutcome> ExportPackageAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var path = Path.GetFullPath(String(input, "path"));
        if (!LogPackage.IsPackage(path)) throw new ToolInputException("The export path must end in .lm.");
        if (File.Exists(path)) throw new ToolInputException($"{path} already exists; choose another name.");
        if (Path.GetDirectoryName(path) is not { } folder || !Directory.Exists(folder))
            throw new ToolInputException($"The folder for {path} does not exist.");
        var compress = !input.TryGetValue("compress", out var c) || c.ValueKind != JsonValueKind.False;
        main.SelectedTab = tab;
        return await main.ExportPackageAsync(path, compress)
            ? new ToolOutcome($"Exported {tab.FileName} with {tab.Bookmarks.Lines.Count:N0} bookmarks to {path}.")
            : new ToolOutcome(main.ErrorMessage ?? "The export was cancelled.", IsError: true);
    }

    private ToolOutcome ListSkills()
    {
        var skills = LoadSkills();
        if (skills.Count == 0) return new ToolOutcome("There are no skills. The user can add them in the AI panel (Skills).");
        var text = new StringBuilder("Skills (the user starts a message with /name to use one; get_skill returns its instructions):\n");
        foreach (var skill in skills) text.AppendLine($"{skill.Name} ({skill.ScopeText}): {skill.Description}");
        return new ToolOutcome(text.ToString());
    }

    /// <summary>
    /// Saves a skill: "scope" local (default; .lignum\skills next to the selected log) or global (every log).
    /// An existing skill of that name is replaced only with "overwrite".
    /// </summary>
    private ToolOutcome SaveSkill(IReadOnlyDictionary<string, JsonElement> input)
    {
        var scope = input.TryGetValue("scope", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()?.Trim().ToLowerInvariant() : null;
        var global = scope switch
        {
            null or "" or "local" => false,
            "global" => true,
            _ => throw new ToolInputException("\"scope\" must be \"local\" or \"global\"."),
        };
        var folder = global
            ? GlobalSkillsFolder
            : LocalSkillsFolder ?? throw new ToolInputException("No log is open, so there is no local skills folder; open a log or save it as global.");
        var description = input.TryGetValue("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
        var file = SkillLibrary.Save(folder, String(input, "name"), description, String(input, "instructions"), Bool(input, "overwrite"));
        var name = SkillLibrary.Normalize(String(input, "name"));
        return new ToolOutcome($"Saved skill /{name} ({(global ? "global" : "local")}) to {file}. The user can run it with /{name}.");
    }

    /// <summary>The instructions of the skill named "name", to follow for the user's request.</summary>
    private ToolOutcome GetSkill(IReadOnlyDictionary<string, JsonElement> input)
    {
        var name = SkillLibrary.Normalize(String(input, "name").TrimStart('/'));
        var skill = LoadSkills().FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ToolInputException($"There is no skill \"{name}\"; use list_skills to see them.");
        var instructions = skill.ReadInstructions();
        if (instructions.Length > SkillLibrary.MaxInstructionChars)
            instructions = instructions[..SkillLibrary.MaxInstructionChars] + "\n(The rest of the skill was cut.)";
        return new ToolOutcome($"Skill \"{skill.Name}\" ({skill.ScopeText}). Follow these instructions:\n\n{instructions}");
    }

    private ToolOutcome GoToLine(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, _) = Selected(input);
        var line = LineIn(tab, Int(input, "line", 0));
        tab.Navigation.GoToLine(line);
        return new ToolOutcome($"Moved to line {line + 1}.");
    }

    /// <summary>
    /// Selects text in the tab's main view and scrolls to it: the first occurrence of "text" on "line", the 1-based
    /// columns "start_column" to "end_column" (inclusive) on it, or whole lines "line" to "end_line".
    /// </summary>
    private ToolOutcome SelectText(IReadOnlyDictionary<string, JsonElement> input)
    {
        var (tab, document) = Selected(input);
        var line = LineIn(tab, Int(input, "line", 0));
        FilePosition start, end;
        string what;
        if (input.TryGetValue("text", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } text)
        {
            var lineText = document.ReadLines(line, 1) is [var read] ? read : "";
            var column = lineText.IndexOf(text, StringComparison.Ordinal);
            if (column < 0) column = lineText.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (column < 0) throw new ToolInputException($"\"{text}\" is not on line {line + 1}; use read_lines to check the line.");
            start = new FilePosition(line, column);
            end = new FilePosition(line, column + text.Length);
            what = $"\"{lineText.Substring(column, text.Length)}\" on line {line + 1} (columns {column + 1}–{column + text.Length})";
        }
        else if (input.ContainsKey("start_column") || input.ContainsKey("end_column"))
        {
            var length = document.ReadLines(line, 1) is [var read] ? read.Length : 0;
            var from = Int(input, "start_column", 1);
            var to = Int(input, "end_column", length);
            if (from < 1 || to < from || to > length)
                throw new ToolInputException($"Columns {from}–{to} are not on line {line + 1}, which has {length:N0} characters.");
            start = new FilePosition(line, from - 1);
            end = new FilePosition(line, to);
            what = $"columns {from}–{to} of line {line + 1}";
        }
        else
        {
            var last = input.ContainsKey("end_line") ? LineIn(tab, Int(input, "end_line", 0)) : line;
            if (last < line) throw new ToolInputException("\"end_line\" must not be before \"line\".");
            start = new FilePosition(line, 0);
            end = new FilePosition(last, int.MaxValue); // to the end of the last line
            what = last == line ? $"line {line + 1}" : $"lines {line + 1}–{last + 1}";
        }

        main.SelectedTab = tab;
        tab.Navigation.GoToLine(line); // scrolls the line into view
        tab.Selection.MoveCaret(start, extend: false);
        tab.Selection.MoveCaret(end, extend: true);
        return new ToolOutcome($"Selected {what} in {tab.FileName}.");
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
        if (!await AllowedAsync(new PermissionRequest(AiAccess.Read, Path.GetFullPath(folder), IsFolder: true), token))
            return Denied($"searching {folder}");
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
        foreach (var item in items) text.AppendLine($"line {item.DisplayLine} [{item.Color}]{(item.Note is { } note ? $": {note}" : "")}");
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
