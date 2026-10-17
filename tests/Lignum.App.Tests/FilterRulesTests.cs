using System.Diagnostics;
using System.Text.Json;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.Viewer;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Filter rules (ticket 12): the per-tab rule list, its results in the shared results pane, and copying them.</summary>
public class FilterRulesTests : IDisposable
{
    private const string Log =
        "start\n" +              // 0
        "ERROR db timeout\n" +   // 1
        "error health check\n" + // 2
        "WARN db slow\n" +       // 3
        "INFO health check\n";   // 4

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];
    private readonly FakeClipboard _clipboard = new();

    public FilterRulesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string WriteLog(string content)
    {
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<LogTabViewModel> OpenLog(string content = Log)
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName))
        {
            Copier = new SelectionCopier(_clipboard, new NoPrompt()),
        });
        _owned.Add(main);
        await main.OpenFileAsync(WriteLog(content));
        return main.SelectedTab!;
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the view model.");
            Thread.Sleep(20);
        }
    }

    private static long[] Shown(SearchViewModel search)
    {
        WaitUntil(() => !search.IsSearching);
        if (search.Results is not { } source) return [];
        var lines = new long[source.RowCount];
        for (var i = 0; i < lines.Length; i++) lines[i] = source.Lines[i];
        return lines;
    }

    [Fact]
    public async Task Adding_an_include_rule_lists_the_matching_lines_in_the_results_pane()
    {
        var search = (await OpenLog()).Search;

        search.AddFilterRule(new QueryRule("error"));

        Assert.Equal([1L, 2L], Shown(search));
        Assert.True(search.IsResultsPaneOpen);
        Assert.Equal("Filter: 2 lines", search.SearchStatus);
    }

    [Fact]
    public async Task Include_and_exclude_rules_combine()
    {
        var search = (await OpenLog()).Search;

        search.AddFilterRule(new QueryRule("error"));
        search.AddFilterRule(new QueryRule("warn"));
        search.AddFilterRule(new QueryRule("health", RuleKind.Exclude));

        Assert.Equal([1L, 3L], Shown(search));
    }

    [Fact]
    public async Task Toggling_a_rule_off_and_on_updates_the_results()
    {
        var search = (await OpenLog()).Search;
        search.AddFilterRule(new QueryRule("error"));
        var exclude = search.AddFilterRule(new QueryRule("health", RuleKind.Exclude));
        Assert.Equal([1L], Shown(search));

        exclude.IsEnabled = false;
        Assert.Equal([1L, 2L], Shown(search));

        exclude.IsEnabled = true;
        Assert.Equal([1L], Shown(search));
    }

    [Fact]
    public async Task Editing_a_rule_updates_the_results()
    {
        var search = (await OpenLog()).Search;
        var rule = search.AddFilterRule(new QueryRule("error"));

        rule.MatchCase = true;
        Assert.Equal([2L], Shown(search));

        rule.Text = "^(ERROR|WARN)";
        rule.IsRegex = true;
        Assert.Equal([1L, 3L], Shown(search));
    }

    [Fact]
    public async Task Removing_a_rule_updates_the_results_and_removing_the_last_clears_them()
    {
        var search = (await OpenLog()).Search;
        var include = search.AddFilterRule(new QueryRule("error"));
        var exclude = search.AddFilterRule(new QueryRule("health", RuleKind.Exclude));

        search.RemoveFilterRuleCommand.Execute(exclude);
        Assert.Equal([1L, 2L], Shown(search));
        Assert.Single(search.FilterRules);

        search.RemoveFilterRuleCommand.Execute(include);
        Assert.Empty(search.FilterRules);
        Assert.Null(search.Results);
        Assert.Equal("", search.SearchStatus);
    }

    [Fact]
    public async Task Changing_the_rules_cancels_the_running_filter()
    {
        // A document that is never indexed keeps every query waiting.
        var search = UiContext.None(() => new SearchViewModel(null));
        _owned.Add(search);
        var doc = LogDocument.Open(WriteLog(Log));
        _owned.Add(doc);
        search.SetDocument(doc);

        search.AddFilterRule(new QueryRule("error"));
        var first = search.ActiveQuery!;
        Assert.Equal(QueryState.Running, first.State);

        search.AddFilterRule(new QueryRule("health", RuleKind.Exclude));

        await first.Completion;
        Assert.Equal(QueryState.Cancelled, first.State);
        Assert.NotSame(first, search.ActiveQuery);
        Assert.True(search.IsSearching);
    }

    [Fact]
    public async Task The_add_box_adds_include_and_exclude_rules_with_its_options()
    {
        var search = (await OpenLog()).Search;

        search.NewFilterText = "db|health";
        search.NewFilterIsRegex = true;
        search.NewFilterMatchCase = true;
        search.AddIncludeFilterCommand.Execute(null);
        search.NewFilterText = "timeout";
        search.AddExcludeFilterCommand.Execute(null);

        Assert.Equal(
            [new QueryRule("db|health", RuleKind.Include, IsRegex: true, MatchCase: true),
             new QueryRule("timeout", RuleKind.Exclude, IsRegex: true, MatchCase: true)],
            search.GetFilterRules());
        Assert.Equal("", search.NewFilterText);
        Assert.Equal([2L, 3L, 4L], Shown(search));
    }

    [Fact]
    public async Task The_add_box_ignores_empty_text()
    {
        var search = (await OpenLog()).Search;

        search.AddIncludeFilterCommand.Execute(null);

        Assert.Empty(search.FilterRules);
    }

    [Fact]
    public async Task An_invalid_regex_rule_shows_an_error_and_keeps_the_last_results()
    {
        var search = (await OpenLog()).Search;
        var rule = search.AddFilterRule(new QueryRule("error"));
        Assert.Equal([1L, 2L], Shown(search));

        rule.IsRegex = true;
        rule.Text = "a(b";

        Assert.NotNull(search.FilterError);
        Assert.Equal([1L, 2L], Shown(search));

        rule.Text = "db";
        Assert.Null(search.FilterError);
        Assert.Equal([1L, 3L], Shown(search));
    }

    [Fact]
    public async Task Filter_and_find_all_share_the_results_pane_and_the_search_runs_inside_the_filter()
    {
        var search = (await OpenLog()).Search;
        search.AddFilterRule(new QueryRule("error"));
        Assert.Equal([1L, 2L], Shown(search));

        search.FindText = "health";
        await search.FindAllCommand.ExecuteAsync(null);
        Assert.Equal([2L], Shown(search)); // ticket 08: the search runs inside the filtered lines

        // Changing a rule or a find option re-runs the combined query.
        search.FilterRules[0].Text = "check";
        Assert.Equal([2L, 4L], Shown(search));
        search.MatchCase = true;
        Assert.Equal([2L, 4L], Shown(search));
    }

    [Fact]
    public async Task Clicking_a_filtered_line_navigates_to_its_original_line()
    {
        var search = (await OpenLog()).Search;
        long? navigated = null;
        search.NavigateRequested += (_, line) => navigated = line;
        search.AddFilterRule(new QueryRule("health"));
        Shown(search);

        search.GoToResult(1);

        Assert.Equal(4L, navigated);
    }

    [Fact]
    public async Task Filtered_lines_highlight_the_include_matches()
    {
        var search = (await OpenLog()).Search;
        search.AddFilterRule(new QueryRule("db"));
        search.AddFilterRule(new QueryRule("slow", RuleKind.Exclude));
        Shown(search);

        Assert.Equal([new TextMatch(6, 2)], search.Highlight!.FindMatches("ERROR db timeout"));
    }

    [Fact]
    public async Task The_rules_round_trip_as_plain_serializable_records()
    {
        var search = (await OpenLog()).Search;
        search.AddFilterRule(new QueryRule("error", IsRegex: true));
        search.AddFilterRule(new QueryRule("health", RuleKind.Exclude, MatchCase: true, IsEnabled: false));

        var json = JsonSerializer.Serialize(search.GetFilterRules());
        var other = (await OpenLog()).Search;
        other.SetFilterRules(JsonSerializer.Deserialize<List<QueryRule>>(json)!);

        Assert.Equal(search.GetFilterRules(), other.GetFilterRules());
        Assert.Equal([1L, 2L], Shown(other));
    }

    [Fact]
    public async Task Selected_filtered_lines_are_copied_without_the_lines_between_them()
    {
        var tab = await OpenLog();
        tab.Search.AddFilterRule(new QueryRule("error"));
        tab.Search.AddFilterRule(new QueryRule("INFO"));
        Shown(tab.Search);

        // From "db timeout" on line 1 to "INFO" on line 4: line 3 is not in the results.
        tab.Search.ResultsSelection.MoveCaret(new FilePosition(1, 6), extend: false);
        tab.Search.ResultsSelection.MoveCaret(new FilePosition(4, 4), extend: true);
        await tab.CopyResultsCommand.ExecuteAsync(null);

        var nl = Environment.NewLine;
        Assert.Equal($"db timeout{nl}error health check{nl}INFO", Assert.Single(_clipboard.Texts));
    }

    [Fact]
    public async Task Select_all_in_the_results_pane_copies_every_filtered_line()
    {
        var tab = await OpenLog();
        tab.Search.AddFilterRule(new QueryRule("health"));
        Shown(tab.Search);

        tab.Search.SelectAllResultsCommand.Execute(null);
        await tab.CopyResultsCommand.ExecuteAsync(null);

        var nl = Environment.NewLine;
        Assert.Equal($"error health check{nl}INFO health check", Assert.Single(_clipboard.Texts));
    }

    [Fact]
    public async Task Copy_for_AI_wraps_the_selected_lines_with_file_name_and_line_numbers()
    {
        var tab = await OpenLog();
        tab.Selection.MoveCaret(new FilePosition(1, 0), extend: false);
        tab.Selection.MoveCaret(new FilePosition(2, 3), extend: true);

        await tab.CopyForAiCommand.ExecuteAsync(null);

        var text = Assert.Single(_clipboard.Texts);
        Assert.Contains("\"app.log\"", text);
        Assert.Contains("2: ERROR db timeout\r\n3: error health check\r\n```", text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.EndsWith("My question: ", text);
        Assert.Equal("Copied 2 lines for AI", tab.CopyMessage);
    }

    [Fact]
    public async Task Copy_for_AI_from_the_results_pane_takes_only_the_result_lines()
    {
        var tab = await OpenLog();
        tab.Search.AddFilterRule(new QueryRule("health"));
        Shown(tab.Search);
        tab.Search.SelectAllResultsCommand.Execute(null);

        await tab.CopyForAiCommand.ExecuteAsync(tab.Search.ResultsSelection);

        var text = Assert.Single(_clipboard.Texts);
        Assert.Contains("3: error health check", text);
        Assert.Contains("5: INFO health check", text);
        Assert.DoesNotContain("4: WARN", text);
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Texts { get; } = [];

        public void SetText(string text) => Texts.Add(text);
    }

    private sealed class NoPrompt : ICopyPrompt
    {
        public LargeCopyChoice AskLargeCopy(long approximateBytes) => LargeCopyChoice.Cancel;

        public string? AskSavePath() => null;
    }
}
