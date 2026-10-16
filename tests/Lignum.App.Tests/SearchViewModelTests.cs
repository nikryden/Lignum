using System.Diagnostics;
using Lignum.App.ViewModels;

namespace Lignum.App.Tests;

/// <summary>Find all, the results pane state and F3 / Shift+F3 navigation.</summary>
public class SearchViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public SearchViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<MainViewModel> OpenLog(string content)
    {
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, content);
        var vm = new MainViewModel();
        await vm.OpenFileAsync(path);
        return vm;
    }

    /// <summary>Search updates are posted to the captured context; give them time to land.</summary>
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
        using var vm = await OpenLog("start\nerror one\nok\nERROR two\n");
        var search = vm.Search;
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
        using var vm = await OpenLog("a\nx\nb\nx\n");
        var search = vm.Search;
        await FindAll(search, "x");
        var navigated = new List<long>();
        search.NavigateRequested += (_, line) => navigated.Add(line);

        search.GoToResult(1);

        Assert.Equal([3L], navigated);
        Assert.Equal(3, search.CurrentLine);
    }

    [Fact]
    public async Task F3_and_Shift_F3_step_through_the_matches_and_wrap_around()
    {
        using var vm = await OpenLog("x\na\nx\nb\nx\n"); // matches on lines 0, 2, 4
        var search = vm.Search;
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
        using var vm = await OpenLog("x\na\nb\nx\nc\nx\n"); // matches on lines 0, 3, 5
        var search = vm.Search;
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
        using var vm = await OpenLog("a\n");

        await FindAll(vm.Search, "");

        Assert.Null(vm.Search.Results);
        Assert.False(vm.Search.IsResultsPaneOpen);
    }

    [Fact]
    public async Task Opening_another_file_clears_the_results()
    {
        using var vm = await OpenLog("x\n");
        await FindAll(vm.Search, "x");

        var other = Path.Combine(_dir, "other.log");
        File.WriteAllText(other, "y\n");
        await vm.OpenFileAsync(other);

        Assert.Null(vm.Search.Results);
        Assert.Equal(0, vm.Search.MatchCount);
        Assert.Equal("", vm.Search.SearchStatus);
    }

    [Fact]
    public async Task The_results_pane_can_be_collapsed_and_reopened()
    {
        using var vm = await OpenLog("x\n");
        await FindAll(vm.Search, "x");

        vm.Search.ToggleResultsPaneCommand.Execute(null);
        Assert.False(vm.Search.IsResultsPaneOpen);
        vm.Search.ToggleResultsPaneCommand.Execute(null);
        Assert.True(vm.Search.IsResultsPaneOpen);
    }

    [Fact]
    public async Task A_search_can_be_cancelled()
    {
        // Indexing a document that is never indexed keeps the query waiting, so it is still running when cancelled.
        using var vm = new MainViewModel();
        var search = vm.Search;
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, "x\n");
        using var doc = Lignum.Core.LogDocument.Open(path);
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
