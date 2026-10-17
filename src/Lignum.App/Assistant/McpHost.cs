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

    [McpServerTool(Name = "add_bookmark"), Description("Bookmark a line in Lignum's selected tab, optionally with a short note.")]
    public Task<string> AddBookmark(
        [Description("1-based line number.")] int line,
        [Description("Optional note shown on the bookmark.")] string? note = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, JsonElement> { ["line"] = E(line) };
        if (note is not null) arguments["note"] = E(note);
        return Call("add_bookmark", arguments, cancellationToken);
    }

    [McpServerTool(Name = "go_to_line"), Description("Scroll Lignum's selected tab to a 1-based line and put the caret there.")]
    public Task<string> GoToLine([Description("1-based line number.")] int line, CancellationToken cancellationToken = default) =>
        Call("go_to_line", new() { ["line"] = E(line) }, cancellationToken);

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

    [McpServerTool(Name = "list_bookmarks"), Description("List a tab's bookmarks and their notes.")]
    public Task<string> ListBookmarks([Description("Tab index or file name; default the selected tab.")] string? tab = null,
                                      CancellationToken cancellationToken = default) =>
        Call("list_bookmarks", WithTab(new(), tab), cancellationToken);

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
