using System.Text.Json;
using Lignum.App.Assistant;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core;
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

    private async Task<MainViewModel> Open(bool enabled = true, bool highlighting = false)
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName))
        {
            Highlighting = highlighting
                ? new HighlightingViewModel(new SettingsStore(Path.Combine(_dir, "settings"), Logger.None)
                    .Open<HighlightPresetsFile>(HighlightPresetsFile.FileName))
                : null,
        });
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
    public async Task The_AI_can_select_text_columns_or_whole_lines()
    {
        var main = await Open();
        var tab = main.SelectedTab!;
        var line = tab.Document!.ReadLines(3, 1)[0]; // "ERROR timeout"

        var byText = await main.Assistant.HandleToolCallAsync(Call("select_text", new { line = 4, text = "timeout" }), default);
        Assert.False(byText.IsError);
        Assert.Equal(new FilePosition(3, line.IndexOf("timeout")), tab.Selection.Start);
        Assert.Equal(new FilePosition(3, line.IndexOf("timeout") + 7), tab.Selection.End);

        await main.Assistant.HandleToolCallAsync(Call("select_text", new { line = 4, start_column = 1, end_column = 5 }), default);
        Assert.Equal((new FilePosition(3, 0), new FilePosition(3, 5)), (tab.Selection.Start, tab.Selection.End));

        await main.Assistant.HandleToolCallAsync(Call("select_text", new { line = 2, end_line = 3 }), default);
        Assert.Equal(new FilePosition(1, 0), tab.Selection.Start);
        Assert.Equal(2, tab.Selection.End.Line);
    }

    [Fact]
    public async Task Selecting_text_that_is_not_there_is_an_error_result()
    {
        var main = await Open();

        var missing = await main.Assistant.HandleToolCallAsync(Call("select_text", new { line = 4, text = "nope" }), default);
        var columns = await main.Assistant.HandleToolCallAsync(Call("select_text", new { line = 4, start_column = 3, end_column = 999 }), default);
        var backwards = await main.Assistant.HandleToolCallAsync(Call("select_text", new { line = 3, end_line = 2 }), default);

        Assert.True(missing.IsError);
        Assert.Contains("is not on line 4", missing.Content);
        Assert.True(columns.IsError);
        Assert.True(backwards.IsError);
        Assert.True(main.SelectedTab!.Selection.IsEmpty);
    }

    [Fact]
    public async Task The_AI_can_edit_and_remove_bookmarks()
    {
        var main = await Open();
        var tab = main.SelectedTab!;
        tab.Bookmarks.SetNote(1, "disk");
        tab.Bookmarks.Toggle(3);

        var edited = await main.Assistant.HandleToolCallAsync(Call("edit_bookmark", new { line = 2, note = "disk full", color = "green" }), default);
        Assert.False(edited.IsError);
        Assert.Equal("disk full", tab.Bookmarks.NoteFor(1));
        Assert.Equal(BookmarkColor.Green, tab.Bookmarks.ColorFor(1));

        var missing = await main.Assistant.HandleToolCallAsync(Call("edit_bookmark", new { line = 5, note = "x" }), default);
        Assert.True(missing.IsError);
        Assert.DoesNotContain(4L, tab.Bookmarks.Lines); // editing never adds a bookmark

        await main.Assistant.HandleToolCallAsync(Call("remove_bookmark", new { line = 2 }), default);
        Assert.Equal([3L], tab.Bookmarks.Lines);
        Assert.True((await main.Assistant.HandleToolCallAsync(Call("remove_bookmark", new { line = 2 }), default)).IsError);

        await main.Assistant.HandleToolCallAsync(Call("remove_bookmark", new { all = true }), default);
        Assert.Empty(tab.Bookmarks.Lines);
    }

    [Fact]
    public async Task The_AI_can_read_the_users_selection()
    {
        var main = await Open();
        var tab = main.SelectedTab!;

        var none = await main.Assistant.HandleToolCallAsync(Call("get_selection", new { }), default);
        Assert.Contains("Nothing is selected", none.Content);

        tab.Selection.MoveCaret(new FilePosition(1, 6), extend: false);
        tab.Selection.MoveCaret(new FilePosition(3, 5), extend: true);
        var selected = await main.Assistant.HandleToolCallAsync(Call("get_selection", new { }), default);

        Assert.Contains("2: disk full", selected.Content);
        Assert.Contains("3: ok", selected.Content);
        Assert.Contains("4: ERROR", selected.Content);
        Assert.DoesNotContain("timeout", selected.Content);
    }

    [Fact]
    public async Task The_AI_can_hide_non_matching_lines_and_show_a_full_line()
    {
        var main = await Open();
        var tab = main.SelectedTab!;

        await main.Assistant.HandleToolCallAsync(Call("set_hide_non_matching", new { enabled = true }), default);
        Assert.True(tab.HideNonMatchingLines);

        main.DetailPane.IsOpen = false;
        await main.Assistant.HandleToolCallAsync(Call("show_full_line", new { line = 4 }), default);
        await tab.Detail.WhenLoaded;
        Assert.True(main.DetailPane.IsOpen);
        Assert.Equal("ERROR timeout", tab.Detail.Text);
    }

    [Fact]
    public async Task The_AI_can_list_remove_and_clear_highlight_rules()
    {
        var main = await Open(highlighting: true);
        var highlighting = Assert.IsType<HighlightingViewModel>(main.Highlighting);
        highlighting.Rules.Clear();
        highlighting.AddRule("ok");
        highlighting.AddRule("ERROR");

        var list = await main.Assistant.HandleToolCallAsync(Call("list_highlight_rules", new { }), default);
        Assert.Contains("1. \"ERROR\"", list.Content);

        await main.Assistant.HandleToolCallAsync(Call("remove_highlight_rule", new { index = 1 }), default);
        Assert.Equal(["ok"], highlighting.Rules.Select(r => r.Pattern));
        Assert.True((await main.Assistant.HandleToolCallAsync(Call("remove_highlight_rule", new { index = 5 }), default)).IsError);

        await main.Assistant.HandleToolCallAsync(Call("clear_highlight_rules", new { }), default);
        Assert.Empty(highlighting.Rules);
    }

    [Fact]
    public async Task The_AI_can_open_and_close_files()
    {
        var main = await Open();
        var other = Path.Combine(_dir, "other.log");
        File.WriteAllLines(other, ["x"]);

        var opened = await main.Assistant.HandleToolCallAsync(Call("open_file", new { path = other }), default);
        Assert.False(opened.IsError, opened.Content);
        Assert.Equal("other.log", main.SelectedTab!.FileName);
        Assert.True((await main.Assistant.HandleToolCallAsync(Call("open_file", new { path = other + ".missing" }), default)).IsError);

        await main.Assistant.HandleToolCallAsync(Call("close_tab", new { tab = "other.log" }), default);
        Assert.Equal(["app.log"], main.Tabs.Select(t => t.FileName));
    }

    [Fact]
    public async Task The_AI_can_export_a_package_with_bookmark_colors_but_never_overwrites()
    {
        var main = await Open();
        main.SelectedTab!.Bookmarks.SetColor(1, BookmarkColor.Red);
        var package = Path.Combine(_dir, "out.lm");

        var exported = await main.Assistant.HandleToolCallAsync(Call("export_package", new { path = package }), default);
        Assert.False(exported.IsError, exported.Content);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(package))
        using (var reader = new StreamReader(zip.GetEntry(Lignum.App.Packaging.LogPackage.BookmarksEntry)!.Open()))
        {
            Assert.Contains("Red", reader.ReadToEnd());
        }

        var again = await main.Assistant.HandleToolCallAsync(Call("export_package", new { path = package }), default);
        var wrongType = await main.Assistant.HandleToolCallAsync(Call("export_package", new { path = Path.Combine(_dir, "out.zip") }), default);
        Assert.True(again.IsError);
        Assert.Contains("already exists", again.Content);
        Assert.True(wrongType.IsError);
    }

    [Fact]
    public void Every_MCP_tool_that_works_on_one_tab_takes_a_tab_argument()
    {
        // Tools for the whole app; every other tool acts on one tab and must pass "tab" on, or an AI's choice of
        // tab is silently dropped and the selected tab is changed instead.
        string[] appWide =
        [
            "list_tabs", "search_all_tabs", "find_in_files", "open_file", "add_highlight_rule", "list_highlight_rules",
            "remove_highlight_rule", "clear_highlight_rules", "list_skills", "get_skill", "save_skill",
        ];
        var missing = typeof(LignumMcpTools).GetMethods()
            .Select(m => (Method: m, Tool: m.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)
                .Cast<ModelContextProtocol.Server.McpServerToolAttribute>().SingleOrDefault()))
            .Where(t => t.Tool is not null && !appWide.Contains(t.Tool.Name))
            .Where(t => t.Method.GetParameters().All(p => p.Name != "tab"))
            .Select(t => t.Tool!.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Add_bookmark_and_go_to_line_act_on_the_named_tab_not_the_selected_one()
    {
        var main = await Open();
        var other = Path.Combine(_dir, "other.log");
        File.WriteAllLines(other, ["a", "b", "c", "d"]);
        await main.OpenFileAsync(other);
        var app = main.Tabs[0];

        var added = await main.Assistant.HandleToolCallAsync(Call("add_bookmark", new { line = 3, tab = "app.log" }), default);
        await main.Assistant.HandleToolCallAsync(Call("go_to_line", new { line = 4, tab = 0 }), default);

        Assert.Contains("app.log", added.Content);
        Assert.Equal([2L], app.Bookmarks.Lines);
        Assert.Empty(main.Tabs[1].Bookmarks.Lines);
        Assert.Equal(3, app.Navigation.Caret.Line);
    }

    [Fact]
    public async Task The_AI_can_color_a_bookmark_and_an_unknown_color_is_an_error_result()
    {
        var main = await Open();

        await main.Assistant.HandleToolCallAsync(Call("add_bookmark", new { line = 2, color = "red" }), default);
        var bad = await main.Assistant.HandleToolCallAsync(Call("add_bookmark", new { line = 3, color = "pink" }), default);
        var list = await main.Assistant.HandleToolCallAsync(Call("list_bookmarks", new { }), default);

        var tab = main.SelectedTab!;
        Assert.Equal(BookmarkColor.Red, tab.Bookmarks.ColorFor(1));
        Assert.True(bad.IsError);
        Assert.Equal([1L], tab.Bookmarks.Lines); // the bad call bookmarked nothing
        Assert.Contains("line 2 [Red]", list.Content);
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
        Assert.Contains("line 2 [Blue]: disk problem", bookmarks.Content);
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
