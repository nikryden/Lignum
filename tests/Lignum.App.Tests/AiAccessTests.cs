using System.Text.Json;
using Lignum.App.Assistant;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>AI access (ticket 05): AI tools call Lignum's tools over MCP, relayed to the running window by a pipe.</summary>
public sealed class AiAccessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public AiAccessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task<MainViewModel> Open(bool enabled = true)
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllLines(path, ["start", "ERROR disk full", "ok", "ERROR timeout", "done"]);
        await main.OpenFileAsync(path);
        main.Assistant.IsEnabled = enabled;
        return main;
    }

    private static ToolCall Call(string tool, object arguments) =>
        new(tool, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!);

    private static Dictionary<string, JsonElement> Args(object arguments) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!;

    [Fact]
    public async Task Search_reports_matches_and_the_panel_lists_what_was_read()
    {
        var main = await Open();

        var outcome = await main.Assistant.HandleToolCallAsync(Call("search", new { pattern = "ERROR" }), default);

        Assert.False(outcome.IsError);
        Assert.Contains("2 matching lines", outcome.Content);
        Assert.Contains("4: ERROR timeout", outcome.Content);
        Assert.Equal(["AI access is on.", "Searching for \"ERROR\"", "Read 2 lines from app.log"],
            main.Assistant.Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task Tools_change_the_tab_and_bad_input_is_an_error_result()
    {
        var main = await Open();

        await main.Assistant.HandleToolCallAsync(Call("add_filter_rule", new { text = "ERROR", kind = "include" }), default);
        await main.Assistant.HandleToolCallAsync(Call("add_bookmark", new { line = 4, note = "timeout here" }), default);
        var bad = await main.Assistant.HandleToolCallAsync(Call("go_to_line", new { line = 99 }), default);
        var read = await main.Assistant.HandleToolCallAsync(Call("read_lines", new { first_line = 4, count = 500 }), default);

        var tab = main.SelectedTab!;
        Assert.Equal("ERROR", Assert.Single(tab.Search.FilterRules).Text);
        Assert.Equal("timeout here", tab.Bookmarks.NoteFor(3));
        Assert.True(bad.IsError);
        Assert.Contains("outside the file", bad.Content);
        Assert.Equal(["4: ERROR timeout", "5: done"], read.Content.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task The_AI_can_search_all_tabs_and_work_on_any_tab()
    {
        var main = await Open();
        var other = Path.Combine(_dir, "b.log");
        File.WriteAllLines(other, ["BuSsBBNDR started", "ok", "error BuSsBBNDR"]);
        await main.OpenFileAsync(other); // b.log is now selected

        var all = await main.Assistant.HandleToolCallAsync(Call("search_all_tabs", new { pattern = "error" }), default);
        var inA = await main.Assistant.HandleToolCallAsync(Call("search", new { pattern = "timeout", tab = "app.log" }), default);
        var byIndex = await main.Assistant.HandleToolCallAsync(Call("read_lines", new { first_line = 1, count = 1, tab = 0 }), default);
        await main.Assistant.HandleToolCallAsync(Call("select_tab", new { tab = "app.log" }), default);

        Assert.Contains("[0] app.log: 2 matching lines", all.Content);
        Assert.Contains("[1] b.log: 1 matching lines", all.Content);
        Assert.Contains("  3: error BuSsBBNDR", all.Content);
        Assert.Contains("1 matching lines in app.log", inA.Content);
        Assert.Contains("1: start", byIndex.Content);
        Assert.Equal("app.log", main.SelectedTab!.FileName);
        var missing = await main.Assistant.HandleToolCallAsync(Call("search", new { pattern = "x", tab = "nope.log" }), default);
        Assert.True(missing.IsError);
    }

    [Fact]
    public async Task The_AI_can_list_change_and_clear_filter_rules_and_show_a_search()
    {
        var main = await Open();
        var tab = main.SelectedTab!;
        await main.Assistant.HandleToolCallAsync(Call("add_filter_rule", new { text = "ERROR", kind = "include" }), default);
        await main.Assistant.HandleToolCallAsync(Call("add_filter_rule", new { text = "disk", kind = "exclude" }), default);

        var list = await main.Assistant.HandleToolCallAsync(Call("list_filter_rules", new { }), default);
        await main.Assistant.HandleToolCallAsync(Call("set_filter_rule_enabled", new { index = 2, enabled = false }), default);
        var shown = await main.Assistant.HandleToolCallAsync(Call("show_search", new { pattern = "timeout" }), default);
        await main.Assistant.HandleToolCallAsync(Call("remove_filter_rule", new { index = 1 }), default);

        Assert.Contains("1. include \"ERROR\"", list.Content);
        Assert.Contains("2. exclude \"disk\"", list.Content);
        Assert.Contains("Filter: 1 rule · Search: timeout · 1 match", shown.Content);
        Assert.True(tab.Search.IsResultsPaneOpen);
        var remaining = Assert.Single(tab.Search.FilterRules);
        Assert.Equal("disk", remaining.Text);
        Assert.False(remaining.IsEnabled);

        await main.Assistant.HandleToolCallAsync(Call("clear_filter_rules", new { }), default);
        Assert.Empty(tab.Search.FilterRules);
    }

    [Fact]
    public async Task The_AI_can_find_in_files_and_list_bookmarks()
    {
        var main = await Open();
        var folder = Path.Combine(_dir, "logs");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "x.log"), ["fine", "ERROR in x"]);
        File.WriteAllLines(Path.Combine(folder, "y.log"), ["nothing"]);
        main.SelectedTab!.Bookmarks.SetNote(1, "disk problem");

        var files = await main.Assistant.HandleToolCallAsync(Call("find_in_files", new { folder, pattern = "error" }), default);
        var bookmarks = await main.Assistant.HandleToolCallAsync(Call("list_bookmarks", new { }), default);

        Assert.StartsWith("1 of 2 files match", files.Content);
        Assert.Contains("x.log: 1 matching lines", files.Content);
        Assert.Contains("  2: ERROR in x", files.Content);
        Assert.Contains("line 2: disk problem", bookmarks.Content);
    }

    [Fact]
    public async Task Calls_are_refused_while_ai_access_is_off()
    {
        var main = await Open(enabled: false);

        var outcome = await main.Assistant.HandleToolCallAsync(Call("list_tabs", new { }), default);

        Assert.True(outcome.IsError);
        Assert.Equal(AiPipeClient.NotAvailableMessage, outcome.Content);
    }

    [Fact]
    public async Task An_mcp_process_reaches_the_running_lignum_over_the_pipe_only_while_it_is_on()
    {
        var main = await Open();
        var pipe = "lignum-test-" + Guid.NewGuid().ToString("N");
        main.Assistant.PipeName = pipe;
        using var client = new AiPipeClient(pipe, connectTimeoutMilliseconds: 500);

        var tabs = await client.CallAsync("list_tabs", Args(new { }), default);
        var search = await client.CallAsync("search", Args(new { pattern = "timeout" }), default);
        main.Assistant.TurnOffCommand.Execute(null);
        var off = await client.CallAsync("list_tabs", Args(new { }), default);

        Assert.Equal("0: app.log (5 lines) [selected]", tabs.Content.Trim());
        Assert.Contains("1 matching lines", search.Content);
        Assert.True(off.IsError);
        Assert.Equal(AiPipeClient.NotAvailableMessage, off.Content);
    }

    [Fact]
    public async Task Setup_snippets_start_lignum_in_mcp_mode()
    {
        var main = await Open();

        Assert.Contains("\"args\": [\"--mcp\"]", main.Assistant.SetupSnippet("vscode"));
        Assert.Contains("\"servers\"", main.Assistant.SetupSnippet("vscode"));
        Assert.Contains("\"mcpServers\"", main.Assistant.SetupSnippet("claude-desktop"));
        Assert.StartsWith("claude mcp add lignum -- \"", main.Assistant.SetupSnippet("claude-code"));
        Assert.EndsWith(" --mcp", main.Assistant.SetupSnippet("claude-code"));
    }
}
