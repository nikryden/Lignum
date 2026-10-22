using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Lignum.App.Assistant;

/// <summary>
/// <c>Lignum.exe --mcp</c> (ticket 05): an MCP server on stdin/stdout, started by an AI tool (GitHub Copilot,
/// Claude Desktop, Claude Code, Cursor...). It owns no window; every tool call goes to the running Lignum.
/// </summary>
internal static class McpHost
{
    public static async Task RunAsync(string pipeName)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders(); // stdout carries the protocol: nothing else may write to it
        builder.Services.AddSingleton(new AiPipeClient(pipeName));
        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<LignumMcpTools>();
        await builder.Build().RunAsync();
    }
}

/// <summary>The tools AI agents see. Each one forwards to <see cref="AssistantTools"/> in the running Lignum.</summary>
[McpServerToolType]
public sealed class LignumMcpTools(AiPipeClient lignum)
{
    [McpServerTool(Name = "list_tabs"), Description("List the log files open in Lignum: tab index, file name, line count and which tab is selected.")]
    public Task<string> ListTabs(CancellationToken cancellationToken) => Call("list_tabs", new(), cancellationToken);

    [McpServerTool(Name = "search"), Description("Search the log file in one of Lignum's tabs (default the selected one). Returns the number of matching lines and the first matches with their 1-based line numbers. Patterns are .NET regular expressions when isRegex is true.")]
    public Task<string> Search(
        [Description("Text or .NET regex to find.")] string pattern,
        [Description("Treat pattern as a regular expression.")] bool isRegex = false,
        [Description("Case-sensitive.")] bool matchCase = false,
        [Description("How many matching lines to return (at most 200).")] int maxLines = 50,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default) =>
        Call("search", WithTab(new() { ["pattern"] = E(pattern), ["is_regex"] = E(isRegex), ["match_case"] = E(matchCase), ["max_lines"] = E(maxLines) }, tab),
            cancellationToken);

    [McpServerTool(Name = "read_lines"), Description("Read consecutive lines of a log file open in Lignum (default the selected tab), from a 1-based line number (at most 200 lines; long lines are cut).")]
    public Task<string> ReadLines(
        [Description("1-based line number to start at.")] int firstLine,
        [Description("How many lines (at most 200).")] int count = 50,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default) =>
        Call("read_lines", WithTab(new() { ["first_line"] = E(firstLine), ["count"] = E(count) }, tab), cancellationToken);

    [McpServerTool(Name = "add_filter_rule"), Description("Add a filter rule to Lignum's selected tab: include rules keep only matching lines, exclude rules hide them. The user sees the result at once.")]
    public Task<string> AddFilterRule(
        [Description("Text or .NET regex.")] string text,
        [Description("\"include\" or \"exclude\".")] string kind,
        [Description("Treat text as a regular expression.")] bool isRegex = false,
        [Description("Case-sensitive.")] bool matchCase = false,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default) =>
        Call("add_filter_rule", WithTab(new() { ["text"] = E(text), ["kind"] = E(kind), ["is_regex"] = E(isRegex), ["match_case"] = E(matchCase) }, tab),
            cancellationToken);

    [McpServerTool(Name = "add_highlight_rule"), Description("Add a line-coloring rule in Lignum (all tabs). Matching lines get the default highlight colors.")]
    public Task<string> AddHighlightRule(
        [Description("Text or .NET regex.")] string pattern,
        [Description("Treat pattern as a regular expression.")] bool isRegex = false,
        [Description("Case-sensitive.")] bool matchCase = false,
        CancellationToken cancellationToken = default) =>
        Call("add_highlight_rule", new() { ["pattern"] = E(pattern), ["is_regex"] = E(isRegex), ["match_case"] = E(matchCase) },
            cancellationToken);

    [McpServerTool(Name = "add_bookmark"), Description("Bookmark a line in a Lignum tab (default the selected one), optionally with a short note and a color.")]
    public Task<string> AddBookmark(
        [Description("1-based line number.")] int line,
        [Description("Optional note shown on the bookmark.")] string? note = null,
        [Description("Optional marker color: Blue (default), Red, Yellow, Green or Purple.")] string? color = null,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, JsonElement> { ["line"] = E(line) };
        if (note is not null) arguments["note"] = E(note);
        if (color is not null) arguments["color"] = E(color);
        return Call("add_bookmark", WithTab(arguments, tab), cancellationToken);
    }

    [McpServerTool(Name = "go_to_line"), Description("Scroll a Lignum tab (default the selected one) to a 1-based line and put the caret there.")]
    public Task<string> GoToLine([Description("1-based line number.")] int line,
                                 [Description("Tab index or file name; default the selected tab.")] string? tab = null,
                                 CancellationToken cancellationToken = default) =>
        Call("go_to_line", WithTab(new() { ["line"] = E(line) }, tab), cancellationToken);

    [McpServerTool(Name = "select_text"), Description("Select text in Lignum's main view and scroll to it, so the user sees it highlighted (Ctrl+C copies it, the detail pane shows it). Give \"text\" to select its first occurrence on the line, or columns, or \"endLine\" for whole lines; with only \"line\" the whole line is selected.")]
    public Task<string> SelectText(
        [Description("1-based line number.")] int line,
        [Description("Text on that line to select (first occurrence; exact case preferred).")] string? text = null,
        [Description("1-based first column to select.")] int? startColumn = null,
        [Description("1-based last column to select (inclusive).")] int? endColumn = null,
        [Description("1-based last line, to select whole lines from line to endLine.")] int? endLine = null,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, JsonElement> { ["line"] = E(line) };
        if (text is not null) arguments["text"] = E(text);
        if (startColumn is { } from) arguments["start_column"] = E(from);
        if (endColumn is { } to) arguments["end_column"] = E(to);
        if (endLine is { } last) arguments["end_line"] = E(last);
        return Call("select_text", WithTab(arguments, tab), cancellationToken);
    }

    [McpServerTool(Name = "select_tab"), Description("Switch Lignum to another open tab (index from list_tabs, or file name).")]
    public Task<string> SelectTab([Description("Tab index from list_tabs, or the file name.")] string tab,
                                  CancellationToken cancellationToken = default) =>
        Call("select_tab", new() { ["tab"] = E(tab) }, cancellationToken);

    [McpServerTool(Name = "search_all_tabs"), Description("Search every log file open in Lignum at once (like Find in all tabs). Returns the number of matching lines per file and the first matches with 1-based line numbers.")]
    public Task<string> SearchAllTabs(
        [Description("Text or .NET regex to find.")] string pattern,
        [Description("Treat pattern as a regular expression.")] bool isRegex = false,
        [Description("Case-sensitive.")] bool matchCase = false,
        [Description("How many matching lines to return per tab (at most 100).")] int maxLinesPerTab = 20,
        CancellationToken cancellationToken = default) =>
        Call("search_all_tabs", new() { ["pattern"] = E(pattern), ["is_regex"] = E(isRegex), ["match_case"] = E(matchCase), ["max_lines_per_tab"] = E(maxLinesPerTab) },
            cancellationToken);

    [McpServerTool(Name = "find_in_files"), Description("Search the log files in a folder that are not open in Lignum (like Find in files). Binary files are skipped. Returns matching files with their match counts and first matches.")]
    public Task<string> FindInFiles(
        [Description("Folder to search.")] string folder,
        [Description("Text or .NET regex to find.")] string pattern,
        [Description("Which files, e.g. \"*.log;*.txt\" (empty: every file).")] string filePatterns = "*.log;*.txt",
        [Description("Also search subfolders.")] bool includeSubfolders = true,
        [Description("Treat pattern as a regular expression.")] bool isRegex = false,
        [Description("Case-sensitive.")] bool matchCase = false,
        [Description("How many matching lines to return per file (at most 50).")] int maxLinesPerFile = 10,
        [Description("At most this many files are searched (at most 1000).")] int maxFiles = 200,
        CancellationToken cancellationToken = default) =>
        Call("find_in_files", new()
        {
            ["folder"] = E(folder), ["pattern"] = E(pattern), ["file_patterns"] = E(filePatterns), ["include_subfolders"] = E(includeSubfolders),
            ["is_regex"] = E(isRegex), ["match_case"] = E(matchCase), ["max_lines_per_file"] = E(maxLinesPerFile), ["max_files"] = E(maxFiles),
        }, cancellationToken);

    [McpServerTool(Name = "show_search"), Description("Run a search in a tab's own find box so the user sees the results in Lignum's results pane, with F3 to step through them. With filter rules active, it searches inside the filtered lines unless insideFilter is false. An empty pattern clears the search.")]
    public Task<string> ShowSearch(
        [Description("Text or .NET regex to find; empty clears the search.")] string pattern,
        [Description("Treat pattern as a regular expression.")] bool isRegex = false,
        [Description("Case-sensitive.")] bool matchCase = false,
        [Description("Search only the lines the filter rules show (default true).")] bool insideFilter = true,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default) =>
        Call("show_search", WithTab(new() { ["pattern"] = E(pattern), ["is_regex"] = E(isRegex), ["match_case"] = E(matchCase), ["inside_filter"] = E(insideFilter) }, tab),
            cancellationToken);

    [McpServerTool(Name = "list_filter_rules"), Description("List a tab's filter rules with their numbers, kinds (include/exclude), options and on/off state.")]
    public Task<string> ListFilterRules([Description("Tab index or file name; default the selected tab.")] string? tab = null,
                                        CancellationToken cancellationToken = default) =>
        Call("list_filter_rules", WithTab(new(), tab), cancellationToken);

    [McpServerTool(Name = "remove_filter_rule"), Description("Remove a filter rule by its number from list_filter_rules.")]
    public Task<string> RemoveFilterRule([Description("1-based rule number from list_filter_rules.")] int index,
                                         [Description("Tab index or file name; default the selected tab.")] string? tab = null,
                                         CancellationToken cancellationToken = default) =>
        Call("remove_filter_rule", WithTab(new() { ["index"] = E(index) }, tab), cancellationToken);

    [McpServerTool(Name = "set_filter_rule_enabled"), Description("Turn a filter rule on or off by its number from list_filter_rules.")]
    public Task<string> SetFilterRuleEnabled([Description("1-based rule number from list_filter_rules.")] int index,
                                             [Description("true to turn it on, false to turn it off.")] bool enabled,
                                             [Description("Tab index or file name; default the selected tab.")] string? tab = null,
                                             CancellationToken cancellationToken = default) =>
        Call("set_filter_rule_enabled", WithTab(new() { ["index"] = E(index), ["enabled"] = E(enabled) }, tab), cancellationToken);

    [McpServerTool(Name = "clear_filter_rules"), Description("Remove every filter rule from a tab.")]
    public Task<string> ClearFilterRules([Description("Tab index or file name; default the selected tab.")] string? tab = null,
                                         CancellationToken cancellationToken = default) =>
        Call("clear_filter_rules", WithTab(new(), tab), cancellationToken);

    [McpServerTool(Name = "list_bookmarks"), Description("List a tab's bookmarks with their colors and notes.")]
    public Task<string> ListBookmarks([Description("Tab index or file name; default the selected tab.")] string? tab = null,
                                      CancellationToken cancellationToken = default) =>
        Call("list_bookmarks", WithTab(new(), tab), cancellationToken);

    [McpServerTool(Name = "remove_bookmark"), Description("Remove the bookmark (with its note and color) on a line, or every bookmark of the tab with all=true.")]
    public Task<string> RemoveBookmark(
        [Description("1-based line number of the bookmark.")] int line = 0,
        [Description("true to remove every bookmark of the tab.")] bool all = false,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default) =>
        Call("remove_bookmark", WithTab(new() { ["line"] = E(line), ["all"] = E(all) }, tab), cancellationToken);

    [McpServerTool(Name = "edit_bookmark"), Description("Change the note and / or color of an existing bookmark.")]
    public Task<string> EditBookmark(
        [Description("1-based line number of the bookmark.")] int line,
        [Description("New note; an empty string removes the note.")] string? note = null,
        [Description("New color: Blue, Red, Yellow, Green or Purple.")] string? color = null,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, JsonElement> { ["line"] = E(line) };
        if (note is not null) arguments["note"] = E(note);
        if (color is not null) arguments["color"] = E(color);
        return Call("edit_bookmark", WithTab(arguments, tab), cancellationToken);
    }

    [McpServerTool(Name = "get_selection"), Description("What the user has selected in Lignum's main view (and where the caret is), with the selected text (at most 200 lines). Use it when the user refers to \"this\" or \"the selected lines\".")]
    public Task<string> GetSelection([Description("Tab index or file name; default the selected tab.")] string? tab = null, CancellationToken cancellationToken = default) =>
        Call("get_selection", WithTab(new(), tab), cancellationToken);

    [McpServerTool(Name = "set_hide_non_matching"), Description("Turn \"Hide non-matching lines\" on or off: when on, the main view shows only the lines matching the filter rules / search.")]
    public Task<string> SetHideNonMatching(
        [Description("true to show only matching lines, false to show every line.")] bool enabled,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default) =>
        Call("set_hide_non_matching", WithTab(new() { ["enabled"] = E(enabled) }, tab), cancellationToken);

    [McpServerTool(Name = "list_highlight_rules"), Description("List the highlight rules (they apply to every tab) with their numbers, options and on/off state.")]
    public Task<string> ListHighlightRules(CancellationToken cancellationToken = default) =>
        Call("list_highlight_rules", new(), cancellationToken);

    [McpServerTool(Name = "remove_highlight_rule"), Description("Remove a highlight rule by its number from list_highlight_rules.")]
    public Task<string> RemoveHighlightRule([Description("1-based rule number from list_highlight_rules.")] int index,
                                            CancellationToken cancellationToken = default) =>
        Call("remove_highlight_rule", new() { ["index"] = E(index) }, cancellationToken);

    [McpServerTool(Name = "clear_highlight_rules"), Description("Remove every highlight rule.")]
    public Task<string> ClearHighlightRules(CancellationToken cancellationToken = default) =>
        Call("clear_highlight_rules", new(), cancellationToken);

    [McpServerTool(Name = "show_full_line"), Description("Open Lignum's detail pane on a line, to show a very long line in full, wrapped.")]
    public Task<string> ShowFullLine([Description("1-based line number.")] int line, [Description("Tab index or file name; default the selected tab.")] string? tab = null,
                                     CancellationToken cancellationToken = default) =>
        Call("show_full_line", WithTab(new() { ["line"] = E(line) }, tab), cancellationToken);

    [McpServerTool(Name = "open_file"), Description("Open a log file or .lm package in a new Lignum tab (or switch to it if open). Returns once the tab is there; big files keep indexing in the background.")]
    public Task<string> OpenFile([Description("Full path of the file.")] string path, CancellationToken cancellationToken = default) =>
        Call("open_file", new() { ["path"] = E(path) }, cancellationToken);

    [McpServerTool(Name = "close_tab"), Description("Close a Lignum tab. Its bookmarks stay saved.")]
    public Task<string> CloseTab([Description("Tab index or file name; default the selected tab.")] string? tab = null, CancellationToken cancellationToken = default) =>
        Call("close_tab", WithTab(new(), tab), cancellationToken);

    [McpServerTool(Name = "export_package"), Description("Export a tab's log with its bookmarks, notes and colors to a new .lm package (File > Export with notes). Never overwrites an existing file. Can take a while for big logs.")]
    public Task<string> ExportPackage(
        [Description("Full path of the new package; must end in .lm and its folder must exist.")] string path,
        [Description("false stores the log uncompressed (faster for huge logs, bigger file). Default true.")] bool compress = true,
        [Description("Tab index or file name; default the selected tab.")] string? tab = null,
        CancellationToken cancellationToken = default) =>
        Call("export_package", WithTab(new() { ["path"] = E(path), ["compress"] = E(compress) }, tab), cancellationToken);

    [McpServerTool(Name = "list_skills"), Description("List the user's Lignum skills (saved instructions, global or next to the selected log) with their descriptions.")]
    public Task<string> ListSkills(CancellationToken cancellationToken = default) =>
        Call("list_skills", new(), cancellationToken);

    [McpServerTool(Name = "get_skill"), Description("Get the instructions of a Lignum skill by name. When the user's message says they chose a skill, call this first and follow what it returns.")]
    public Task<string> GetSkill([Description("The skill's name, as in list_skills (without the /).")] string name,
                                 CancellationToken cancellationToken = default) =>
        Call("get_skill", new() { ["name"] = E(name) }, cancellationToken);

    [McpServerTool(Name = "save_skill"), Description("Save a Lignum skill (instructions the user runs with /name). Local skills go in .lignum\\skills next to the selected log; global ones are for every log. Use this when the user asks for a skill instead of telling them to save a file.")]
    public Task<string> SaveSkill(
        [Description("Short name to type after /, e.g. first-error.")] string name,
        [Description("The instructions the AI follows when the skill is used (Markdown).")] string instructions,
        [Description("One line shown in the / list.")] string? description = null,
        [Description("\"local\" (default, next to the selected log) or \"global\" (every log).")] string? scope = null,
        [Description("true to replace an existing skill of that name; ask the user first.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["name"] = E(name), ["instructions"] = E(instructions), ["overwrite"] = E(overwrite),
        };
        if (description is not null) arguments["description"] = E(description);
        if (scope is not null) arguments["scope"] = E(scope);
        return Call("save_skill", arguments, cancellationToken);
    }

    // File tools (ticket 18): each call is checked against the user's AI permissions; without one, Lignum asks the user.

    [McpServerTool(Name = "list_dir"), Description("List a folder anywhere on this PC (subfolders first, then files with sizes; at most 500 entries). Lignum asks the user for permission unless already granted.")]
    public Task<string> ListDir(
        [Description("Full path of the folder.")] string path,
        CancellationToken cancellationToken = default) =>
        Call("list_dir", new() { ["path"] = E(path) }, cancellationToken);

    [McpServerTool(Name = "read_file"), Description("Read lines of a text file anywhere on this PC (e.g. source code in another repository), with 1-based line numbers; at most 500 lines per call, files up to 50 MB (open bigger logs with open_file). Lignum asks the user for permission unless already granted.")]
    public Task<string> ReadFile(
        [Description("Full path of the file.")] string path,
        [Description("1-based line to start at.")] int firstLine = 1,
        [Description("How many lines (at most 500).")] int count = 200,
        CancellationToken cancellationToken = default) =>
        Call("read_file", new() { ["path"] = E(path), ["first_line"] = E(firstLine), ["count"] = E(count) }, cancellationToken);

    [McpServerTool(Name = "write_file"), Description("Create a text file or replace its whole content. The user sees the content and must allow it. Log files open in Lignum can never be written.")]
    public Task<string> WriteFile(
        [Description("Full path of the file.")] string path,
        [Description("The complete new content.")] string content,
        CancellationToken cancellationToken = default) =>
        Call("write_file", new() { ["path"] = E(path), ["content"] = E(content) }, cancellationToken);

    [McpServerTool(Name = "edit_file"), Description("Replace exact text in a text file: oldText must occur exactly once unless replaceAll is true. Read the file first and copy oldText exactly. The user sees the change and must allow it. Log files open in Lignum can never be written.")]
    public Task<string> EditFile(
        [Description("Full path of the file.")] string path,
        [Description("The exact text to replace (with enough context to be unique).")] string oldText,
        [Description("The text to put in its place (may be empty).")] string newText,
        [Description("Replace every occurrence.")] bool replaceAll = false,
        CancellationToken cancellationToken = default) =>
        Call("edit_file", new() { ["path"] = E(path), ["old_text"] = E(oldText), ["new_text"] = E(newText), ["replace_all"] = E(replaceAll) },
            cancellationToken);

    [McpServerTool(Name = "run_command"), Description("Run a command on this PC (git, PowerShell, cmd, Python, WSL, ...) and get its output and exit code. The user sees the exact command and must allow it. No input can be typed, so use non-interactive options. Output is capped at 100,000 characters.")]
    public Task<string> RunCommand(
        [Description("The command. With shell \"none\" its first word is the program (e.g. \"git log -n 5\", \"python script.py\"); otherwise it is a script for that shell.")] string command,
        [Description("\"none\" (run a program directly), \"pwsh\", \"powershell\", \"cmd\" or \"wsl\" (bash in WSL).")] string shell = "none",
        [Description("Working folder (full path); default the folder of the selected log.")] string? cwd = null,
        [Description("Stop it after this many seconds (at most 600).")] int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, JsonElement> { ["command"] = E(command), ["shell"] = E(shell), ["timeout_seconds"] = E(timeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(cwd)) arguments["cwd"] = E(cwd);
        return Call("run_command", arguments, cancellationToken);
    }

    private static Dictionary<string, JsonElement> WithTab(Dictionary<string, JsonElement> arguments, string? tab)
    {
        if (!string.IsNullOrWhiteSpace(tab)) arguments["tab"] = E(tab);
        return arguments;
    }

    private async Task<string> Call(string tool, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var response = await lignum.CallAsync(tool, arguments, cancellationToken);
        return response.IsError ? throw new McpException(response.Content) : response.Content;
    }

    private static JsonElement E<T>(T value) => JsonSerializer.SerializeToElement(value);
}
