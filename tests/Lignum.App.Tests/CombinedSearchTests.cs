using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.Viewer;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Search and filter together (ticket 08): the filter narrows the log, the search runs inside it.</summary>
public sealed class CombinedSearchTests : IDisposable
{
    private const string Log =
        "start\n" +                          // 0
        "OrderService timeout calling db\n" + // 1
        "HealthCheck OrderService timeout\n" + // 2
        "OrderService ok\n" +                // 3
        "PaymentService timeout\n";          // 4

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public CombinedSearchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task<SearchViewModel> Open() => (await OpenTab()).Tab.Search;

    private MainViewModel Start()
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        return main;
    }

    private async Task<(MainViewModel Main, LogTabViewModel Tab)> OpenTab()
    {
        var main = Start();
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, Log);
        await main.OpenFileAsync(path);
        return (main, main.SelectedTab!);
    }

    private static long[] Rows(LineSource? source)
    {
        if (source is null) return [];
        var lines = new long[source.RowCount];
        for (var i = 0; i < lines.Length; i++) lines[i] = source.Lines[i];
        return lines;
    }

    [Fact]
    public async Task Hiding_non_matching_lines_shows_only_the_results_in_the_main_view()
    {
        var (_, tab) = await OpenTab();
        FilterOrders(tab.Search);
        await Find(tab.Search, "timeout");
        Shown(tab.Search);
        Assert.Equal(5, tab.ViewLines!.RowCount); // off by default: every line

        tab.HideNonMatchingLines = true;
        Assert.Equal([1L], Rows(tab.ViewLines));

        await Find(tab.Search, ""); // the results change: the main view follows
        Shown(tab.Search);
        Assert.Equal([1L, 3L], Rows(tab.ViewLines));
    }

    [Fact]
    public async Task Matches_on_lines_the_filter_hides_are_highlighted_only_when_the_setting_is_on()
    {
        var (main, tab) = await OpenTab();
        FilterOrders(tab.Search);
        await Find(tab.Search, "timeout");
        const string hidden = "HealthCheck OrderService timeout";

        Assert.NotEmpty(tab.Search.Highlight!.FindMatches(hidden)); // on by default

        main.HighlightMatchesOutsideFilter = false;
        Assert.Empty(tab.Search.Highlight!.FindMatches(hidden));
        Assert.NotEmpty(tab.Search.Highlight.FindMatches("OrderService timeout calling db"));
        Assert.False(Start().HighlightMatchesOutsideFilter); // saved in settings.json
    }

    [Fact]
    public async Task The_session_restores_the_search_the_scope_and_hiding()
    {
        var (main, tab) = await OpenTab();
        FilterOrders(tab.Search);
        tab.Search.MatchCase = true;
        await Find(tab.Search, "timeout");
        tab.Search.SearchInsideFilter = false;
        tab.HideNonMatchingLines = true;
        main.SaveSession();

        var restarted = Start();
        await restarted.RestoreSession();

        var search = restarted.SelectedTab!.Search;
        Assert.Equal("timeout", search.FindText);
        Assert.True(search.MatchCase);
        Assert.False(search.SearchInsideFilter);
        Assert.True(restarted.SelectedTab.HideNonMatchingLines);
        Assert.Equal([1L, 2L, 4L], Shown(search));
    }

    private static long[] Shown(SearchViewModel search)
    {
        var clock = Stopwatch.StartNew();
        while (search.IsSearching)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the search.");
            Thread.Sleep(20);
        }

        if (search.Results is not { } source) return [];
        var lines = new long[source.RowCount];
        for (var i = 0; i < lines.Length; i++) lines[i] = source.Lines[i];
        return lines;
    }

    private static async Task Find(SearchViewModel search, string text)
    {
        search.FindText = text;
        await search.FindAllCommand.ExecuteAsync(null);
    }

    private static void FilterOrders(SearchViewModel search)
    {
        search.AddFilterRule(new QueryRule("HealthCheck", RuleKind.Exclude));
        search.AddFilterRule(new QueryRule("OrderService"));
    }

    [Fact]
    public async Task With_both_active_the_results_are_filtered_lines_that_match_the_search()
    {
        var search = await Open();
        FilterOrders(search);
        await Find(search, "timeout");

        Assert.Equal([1L], Shown(search));
        Assert.Equal(1, search.MatchCount);
        Assert.Equal("Filter: 2 rules · Search: timeout · 1 match", search.ResultsHeader);
    }

    [Fact]
    public async Task The_order_does_not_matter_and_changing_a_rule_reruns_the_combined_query()
    {
        var search = await Open();
        await Find(search, "timeout");
        Assert.Equal([1L, 2L, 4L], Shown(search));

        FilterOrders(search);
        Assert.Equal([1L], Shown(search));

        search.FilterRules[0].IsEnabled = false; // HealthCheck lines come back
        Assert.Equal([1L, 2L], Shown(search));
    }

    [Fact]
    public async Task With_only_one_of_them_it_behaves_as_before()
    {
        var search = await Open();
        FilterOrders(search);
        Assert.Equal([1L, 3L], Shown(search));
        Assert.Equal("Filter: 2 rules · 2 lines", search.ResultsHeader);

        await Find(search, "timeout");
        await Find(search, ""); // an empty find box ends the search: the filter's lines again
        Assert.Equal([1L, 3L], Shown(search));
    }

    [Fact]
    public async Task The_scope_toggle_searches_the_whole_file_instead()
    {
        var search = await Open();
        FilterOrders(search);
        await Find(search, "timeout");

        search.ToggleSearchScopeCommand.Execute(null);

        Assert.False(search.SearchInsideFilter);
        Assert.Equal([1L, 2L, 4L], Shown(search));
        Assert.Equal("Search: timeout (whole file) · 3 matches", search.ResultsHeader);
    }

    [Fact]
    public async Task F3_steps_only_through_matches_inside_the_filter()
    {
        var search = await Open();
        FilterOrders(search);
        search.AddFilterRule(new QueryRule("PaymentService")); // lines 1, 3, 4 pass the filter
        await Find(search, "timeout");
        Assert.Equal([1L, 4L], Shown(search));

        search.FindNextCommand.Execute(null);
        Assert.Equal(1L, search.CurrentLine);
        search.FindNextCommand.Execute(null);
        Assert.Equal(4L, search.CurrentLine);
        search.FindNextCommand.Execute(null);
        Assert.Equal(1L, search.CurrentLine); // wraps, never stopping on line 2 (HealthCheck)
    }
}
