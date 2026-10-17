using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Find all, the results pane state and F3 / Shift+F3 navigation (per tab).</summary>
public class SearchViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public SearchViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string WriteLog(string content, string name = "app.log")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<LogTabViewModel> OpenLog(string content)
    {
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        await main.OpenFileAsync(WriteLog(content));
        return main.SelectedTab!;
    }

    /// <summary>The last query update can land just after the command's task completes.</summary>
    private static void WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the view model.");
            Thread.Sleep(20);
        }
    }

    private static async Task FindAll(SearchViewModel search, string text)
    {
        search.FindText = text;
        await search.FindAllCommand.ExecuteAsync(null);
        WaitUntil(() => !search.IsSearching);
    }

    private static long[] ResultLines(SearchViewModel search)
    {
        var source = search.Results!;
        var lines = new long[source.RowCount];
        for (var i = 0; i < lines.Length; i++) lines[i] = source.Lines[i];
        return lines;
    }

    [Fact]
    public async Task Find_all_lists_the_matching_lines_with_their_original_numbers_and_opens_the_pane()
    {
        var tab = await OpenLog("start\nerror one\nok\nERROR two\n");
        var search = tab.Search;
        Assert.False(search.IsResultsPaneOpen);

        await FindAll(search, "error");

        Assert.Equal([1L, 3L], ResultLines(search));
        Assert.Equal(2, search.MatchCount);
        Assert.True(search.IsResultsPaneOpen);
        Assert.Equal("2 matches", search.SearchStatus);
        Assert.NotNull(search.Highlight);
    }

    [Fact]
    public async Task Clicking_a_result_navigates_to_its_line()
    {
        var search = (await OpenLog("a\nx\nb\nx\n")).Search;
        await FindAll(search, "x");
        var navigated = new List<long>();
        search.NavigateRequested += (_, line) => navigated.Add(line);

        search.GoToResult(1);

        Assert.Equal([3L], navigated);
        Assert.Equal(3, search.CurrentLine);
        Assert.Equal(1, search.CurrentResultRow);
    }

    [Fact]
    public async Task F3_and_Shift_F3_step_through_the_matches_and_wrap_around()
    {
        var search = (await OpenLog("x\na\nx\nb\nx\n")).Search; // matches on lines 0, 2, 4
        await FindAll(search, "x");
        var navigated = new List<long>();
        search.NavigateRequested += (_, line) => navigated.Add(line);

        search.FindNextCommand.Execute(null);
        search.FindNextCommand.Execute(null);
        search.FindNextCommand.Execute(null);
        search.FindNextCommand.Execute(null);
        search.FindPreviousCommand.Execute(null);

        Assert.Equal([0L, 2L, 4L, 0L, 4L], navigated);
    }

    [Fact]
    public async Task F3_continues_from_the_line_the_user_last_went_to()
    {
        var search = (await OpenLog("x\na\nb\nx\nc\nx\n")).Search; // matches on lines 0, 3, 5
        await FindAll(search, "x");
        var navigated = new List<long>();
        search.NavigateRequested += (_, line) => navigated.Add(line);

        search.CurrentLine = 2; // e.g. the user clicked line 3 in the main view
        search.FindNextCommand.Execute(null);
        search.CurrentLine = 4;
        search.FindPreviousCommand.Execute(null);

        Assert.Equal([3L, 3L], navigated);
    }

    [Fact]
    public async Task An_empty_find_text_does_not_search()
    {
        var search = (await OpenLog("a\n")).Search;

        await FindAll(search, "");

        Assert.Null(search.Results);
        Assert.False(search.IsResultsPaneOpen);
    }

    [Fact]
    public async Task Each_tab_has_its_own_results()
    {
        var first = await OpenLog("x\n");
        await FindAll(first.Search, "x");

        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings2"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(main);
        await main.OpenFileAsync(WriteLog("y\n", "other.log"));

        Assert.Null(main.SelectedTab!.Search.Results);
        Assert.Equal(1, first.Search.MatchCount);
    }

    [Fact]
    public async Task A_new_document_clears_the_results()
    {
        var search = UiContext.None(() => new SearchViewModel(null));
        _owned.Add(search);
        var doc = LogDocument.Open(WriteLog("x\n"));
        _owned.Add(doc);
        await doc.StartIndexing();
        search.SetDocument(doc);
        await FindAll(search, "x");

        var other = LogDocument.Open(WriteLog("y\n", "other.log"));
        _owned.Add(other);
        search.SetDocument(other);

        Assert.Null(search.Results);
        Assert.Equal(0, search.MatchCount);
        Assert.Equal("", search.SearchStatus);
    }

    [Fact]
    public async Task The_results_pane_can_be_collapsed_and_reopened()
    {
        var search = (await OpenLog("x\n")).Search;
        await FindAll(search, "x");

        search.ToggleResultsPaneCommand.Execute(null);
        Assert.False(search.IsResultsPaneOpen);
        search.ToggleResultsPaneCommand.Execute(null);
        Assert.True(search.IsResultsPaneOpen);
    }

    [Fact]
    public async Task Ctrl_F_opens_the_find_box_and_asks_for_focus_each_time()
    {
        var search = (await OpenLog("x\n")).Search;
        var focusRequests = 0;
        search.FindBoxFocusRequested += (_, _) => focusRequests++;

        search.OpenFindBarCommand.Execute(null);
        search.OpenFindBarCommand.Execute(null);

        Assert.True(search.IsFindBarOpen);
        Assert.Equal(2, focusRequests);
    }

    [Fact]
    public async Task The_case_toggle_applies_to_find_all()
    {
        var search = (await OpenLog("Error\nerror\nERROR\n")).Search;

        search.MatchCase = true;
        await FindAll(search, "error");

        Assert.Equal([1L], ResultLines(search));
    }

    [Fact]
    public async Task The_regex_toggle_applies_to_find_all_and_F3()
    {
        var search = (await OpenLog("id=1\nid=x\nid=22\nid=\\d+\n")).Search;
        var navigated = new List<long>();
        search.NavigateRequested += (_, line) => navigated.Add(line);

        await FindAll(search, @"id=\d+");
        Assert.Equal([3L], ResultLines(search)); // plain text: the literal pattern

        search.IsRegex = true;
        await FindAll(search, @"id=\d+");
        search.FindNextCommand.Execute(null);
        search.FindNextCommand.Execute(null);

        Assert.Equal([0L, 2L], ResultLines(search));
        Assert.Equal([0L, 2L], navigated);
    }

    [Fact]
    public async Task Changing_an_option_reruns_the_last_search()
    {
        var search = (await OpenLog("Error\nerror\n")).Search;
        await FindAll(search, "error");
        Assert.Equal(2, search.MatchCount);

        search.MatchCase = true;
        WaitUntil(() => !search.IsSearching && search.MatchCount == 1);

        Assert.Equal([1L], ResultLines(search));
    }

    [Fact]
    public async Task An_invalid_regex_shows_a_validation_error_and_does_not_search()
    {
        var search = (await OpenLog("a(b\n")).Search;
        search.IsRegex = true;

        await FindAll(search, "a(b");

        Assert.NotNull(search.FindTextError);
        Assert.Contains("Not enough )", search.FindTextError);
        Assert.Null(search.Results);
        Assert.False(search.IsResultsPaneOpen);
    }

    [Fact]
    public async Task The_validation_error_clears_when_the_text_or_options_change()
    {
        var search = (await OpenLog("a(b\n")).Search;
        search.IsRegex = true;
        await FindAll(search, "a(b");

        search.FindText = "a(b)";
        Assert.Null(search.FindTextError);

        await FindAll(search, "a(");
        Assert.NotNull(search.FindTextError);
        search.IsRegex = false;
        WaitUntil(() => !search.IsSearching);
        Assert.Null(search.FindTextError);
    }

    [Fact]
    public async Task A_pattern_NonBacktracking_cannot_run_shows_a_validation_error()
    {
        var search = (await OpenLog("aa\n")).Search;
        search.IsRegex = true;
        search.UseNonBacktracking = true;

        await FindAll(search, @"(a)\1");

        Assert.Contains("NonBacktracking", search.FindTextError);
        Assert.Null(search.Results);
    }

    [Fact]
    public async Task NonBacktracking_finds_the_same_lines()
    {
        var search = (await OpenLog("2024-01-01 ERROR\nINFO\nWARN 2024-02-03\n")).Search;
        search.IsRegex = true;
        await FindAll(search, @"\d{4}-\d\d-\d\d");
        var backtracking = ResultLines(search);

        search.UseNonBacktracking = true;
        await FindAll(search, @"\d{4}-\d\d-\d\d");

        Assert.Equal([0L, 2L], backtracking);
        Assert.Equal(backtracking, ResultLines(search));
    }

    [Fact]
    public async Task A_catastrophic_regex_times_out_with_a_clear_error_and_the_view_model_stays_usable()
    {
        var search = (await OpenLog("ok\n" + new string('a', 5_000) + "!\nmore\n")).Search;
        search.RegexTimeout = TimeSpan.FromMilliseconds(100);
        search.IsRegex = true;

        var clock = Stopwatch.StartNew();
        await FindAll(search, "^(a+)+$");

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
        Assert.False(search.IsSearching);
        Assert.Contains("too long", search.SearchStatus);
        Assert.Null(search.Highlight); // nothing left to re-run the bad pattern while drawing

        await FindAll(search, "more"); // and the next search works
        Assert.Equal([2L], ResultLines(search));
    }

    [Fact]
    public void The_keyboard_toggle_commands_flip_the_options()
    {
        var search = UiContext.None(() => new SearchViewModel(null));
        _owned.Add(search);

        search.ToggleMatchCaseCommand.Execute(null);
        search.ToggleRegexCommand.Execute(null);
        search.ToggleNonBacktrackingCommand.Execute(null);

        Assert.True(search.MatchCase);
        Assert.True(search.IsRegex);
        Assert.True(search.UseNonBacktracking);
    }

    [Fact]
    public async Task Highlighting_uses_a_short_regex_timeout()
    {
        var search = (await OpenLog("abc\n")).Search;
        search.IsRegex = true;

        await FindAll(search, "b");

        Assert.True(search.Highlight!.RegexTimeout <= TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task A_search_can_be_cancelled()
    {
        // A document that is never indexed keeps the query waiting, so it is still running when cancelled.
        var search = UiContext.None(() => new SearchViewModel(null));
        _owned.Add(search);
        var doc = LogDocument.Open(WriteLog("x\n"));
        _owned.Add(doc);
        search.SetDocument(doc);
        search.FindText = "x";
        var running = search.FindAllCommand.ExecuteAsync(null);

        Assert.True(search.CancelSearchCommand.CanExecute(null));
        search.CancelSearchCommand.Execute(null);
        await running;
        WaitUntil(() => !search.IsSearching);

        Assert.Equal("Search cancelled (0 matches)", search.SearchStatus);
    }
}
