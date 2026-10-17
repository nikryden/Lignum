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

    [McpServerTool(Name = "search"), Description("Search the log file in Lignum's selected tab. Returns the number of matching lines and the first matches with their 1-based line numbers. Patterns are .NET regular expressions when isRegex is true.")]
    public Task<string> Search(
        [Description("Text or .NET regex to find.")] string pattern,
        [Description("Treat pattern as a regular expression.")] bool isRegex = false,
        [Description("Case-sensitive.")] bool matchCase = false,
        [Description("How many matching lines to return (at most 200).")] int maxLines = 50,
        CancellationToken cancellationToken = default) =>
        Call("search", new() { ["pattern"] = E(pattern), ["is_regex"] = E(isRegex), ["match_case"] = E(matchCase), ["max_lines"] = E(maxLines) },
            cancellationToken);

    [McpServerTool(Name = "read_lines"), Description("Read consecutive lines of the log file in Lignum's selected tab, from a 1-based line number (at most 200 lines; long lines are cut).")]
    public Task<string> ReadLines(
        [Description("1-based line number to start at.")] int firstLine,
        [Description("How many lines (at most 200).")] int count = 50,
        CancellationToken cancellationToken = default) =>
        Call("read_lines", new() { ["first_line"] = E(firstLine), ["count"] = E(count) }, cancellationToken);

    [McpServerTool(Name = "add_filter_rule"), Description("Add a filter rule to Lignum's selected tab: include rules keep only matching lines, exclude rules hide them. The user sees the result at once.")]
    public Task<string> AddFilterRule(
        [Description("Text or .NET regex.")] string text,
        [Description("\"include\" or \"exclude\".")] string kind,
        [Description("Treat text as a regular expression.")] bool isRegex = false,
        [Description("Case-sensitive.")] bool matchCase = false,
        CancellationToken cancellationToken = default) =>
        Call("add_filter_rule", new() { ["text"] = E(text), ["kind"] = E(kind), ["is_regex"] = E(isRegex), ["match_case"] = E(matchCase) },
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

    private async Task<string> Call(string tool, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var response = await lignum.CallAsync(tool, arguments, cancellationToken);
        return response.IsError ? throw new McpException(response.Content) : response.Content;
    }

    private static JsonElement E<T>(T value) => JsonSerializer.SerializeToElement(value);
}
