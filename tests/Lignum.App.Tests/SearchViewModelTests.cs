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
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
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
