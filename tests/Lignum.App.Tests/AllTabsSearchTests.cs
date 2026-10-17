using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Find in all tabs (ticket 01): one search over every open tab, grouped by file.</summary>
public sealed class AllTabsSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public AllTabsSearchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task<MainViewModel> OpenTwo()
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        var a = Path.Combine(_dir, "a.log");
        var b = Path.Combine(_dir, "b.log");
        File.WriteAllLines(a, ["start", "ERROR disk full", "ok", "error again"]);
        File.WriteAllLines(b, ["nothing here", "Error in b"]);
        await main.OpenFilesAsync([a, b]);
        main.AllTabsSearch.ShowResultsPerTab = false; // most tests check the shared pane
        return main;
    }

    private static async Task Search(AllTabsSearchViewModel search, string text)
    {
        search.FindText = text;
        await search.SearchCommand.ExecuteAsync(null);
    }

    private static string[] Describe(AllTabsSearchViewModel search) =>
    [
        .. search.Rows.Select(r => r switch
        {
            AllTabsFileRow file => file.Header,
            AllTabsMatchRow match => $"  {match.DisplayLine}: {match.Text}",
            _ => "?",
        }),
    ];

    [Fact]
    public async Task One_search_lists_matches_from_every_tab_grouped_by_file()
    {
        var main = await OpenTwo();

        await Search(main.AllTabsSearch, "error");

        Assert.Equal(
            ["a.log: 2 matches", "  2: ERROR disk full", "  4: error again", "b.log: 1 match", "  2: Error in b"],
            Describe(main.AllTabsSearch));
        Assert.Equal("3 matches in 2 files", main.AllTabsSearch.Status);
        Assert.False(main.AllTabsSearch.IsSearching);
    }

    [Fact]
    public async Task A_file_group_collapses_and_matched_text_is_marked()
    {
        var main = await OpenTwo();
        var search = main.AllTabsSearch;
        await Search(search, "error");

        search.ToggleGroupCommand.Execute(search.Rows[0]);
        Assert.Equal(["a.log: 2 matches", "b.log: 1 match", "  2: Error in b"], Describe(search));
        search.ExpandAllCommand.Execute(null);
        Assert.Equal(5, search.Rows.Count);
        search.CollapseAllCommand.Execute(null);
        Assert.Equal(["a.log: 2 matches", "b.log: 1 match"], Describe(search));

        search.ExpandAllCommand.Execute(null);
        var row = search.Rows.OfType<AllTabsMatchRow>().First();
        Assert.Equal([new TextSegment("ERROR", true), new TextSegment(" disk full", false)], row.Segments);
    }

    [Fact]
    public void Per_tab_is_the_default() => Assert.True(
        UiContext.None(() => new MainViewModel(new SettingsStore(Path.Combine(_dir, "s2"), Logger.None)
            .Open<AppSettings>(AppSettings.FileName))).AllTabsSearch.ShowResultsPerTab);

    [Fact]
    public async Task Case_and_regex_options_work_like_single_file_search()
    {
        var main = await OpenTwo();
        main.AllTabsSearch.MatchCase = true;
        await Search(main.AllTabsSearch, "ERROR");
        Assert.Equal(["a.log: 1 match", "  2: ERROR disk full"], Describe(main.AllTabsSearch));

        main.AllTabsSearch.MatchCase = false;
        main.AllTabsSearch.IsRegex = true;
        await Search(main.AllTabsSearch, "^error");
        Assert.Equal(["a.log: 2 matches", "  2: ERROR disk full", "  4: error again", "b.log: 1 match", "  2: Error in b"],
            Describe(main.AllTabsSearch).Take(5));

        await Search(main.AllTabsSearch, "(unclosed");
        Assert.NotNull(main.AllTabsSearch.FindTextError);
        Assert.Empty(main.AllTabsSearch.Rows);
    }

    [Fact]
    public async Task Clicking_a_match_switches_to_its_tab_at_the_line()
    {
        var main = await OpenTwo();
        await Search(main.AllTabsSearch, "error");
        var inA = main.AllTabsSearch.Rows.OfType<AllTabsMatchRow>().First(r => r.Line == 3);
        Assert.NotSame(inA.Tab, main.SelectedTab); // b.log was opened last

        main.AllTabsSearch.GoToCommand.Execute(inA);

        Assert.Same(inA.Tab, main.SelectedTab);
        Assert.Equal(3, inA.Tab!.Navigation.Caret.Line);
    }

    [Fact]
    public async Task Per_tab_mode_runs_the_search_in_each_tabs_own_results_pane()
    {
        var main = await OpenTwo();
        main.AllTabsSearch.ShowResultsPerTab = true;

        await Search(main.AllTabsSearch, "error");

        Assert.Empty(main.AllTabsSearch.Rows);
        Assert.Equal([2L, 1L], main.Tabs.Select(t => t.Search.MatchCount));
        Assert.All(main.Tabs, t => Assert.Equal("error", t.Search.FindText));
        Assert.All(main.Tabs, t => Assert.True(t.Search.IsResultsPaneOpen));
        Assert.Equal("3 matches in 2 tabs: see each tab's results pane", main.AllTabsSearch.Status);
    }

    [Fact]
    public async Task A_closed_tab_is_skipped_and_cancel_stops_the_search()
    {
        var main = await OpenTwo();
        await Search(main.AllTabsSearch, "error");
        var inB = main.AllTabsSearch.Rows.OfType<AllTabsMatchRow>().Last();
        main.CloseTabCommand.Execute(inB.Tab!);

        main.AllTabsSearch.GoToCommand.Execute(inB); // nothing happens
        Assert.DoesNotContain(inB.Tab!, main.Tabs);

        main.AllTabsSearch.CancelCommand.Execute(null);
        Assert.False(main.AllTabsSearch.IsSearching);
    }
}
