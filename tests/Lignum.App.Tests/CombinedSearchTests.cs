using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
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
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<SearchViewModel> Open()
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, Log);
        await main.OpenFileAsync(path);
        return main.SelectedTab!.Search;
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
