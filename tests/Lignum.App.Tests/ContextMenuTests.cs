using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>
/// The right-click menu and Ctrl+Shift+C (ticket 15): which text "copy to search" and the add-as-filter /
/// highlight items take (the selection, its first line, or the word under the cursor) and what they do with it.
/// </summary>
public class ContextMenuTests : IDisposable
{
    private const string Log =
        "start req=a17 begin\n" +          // 0
        "ERROR req=b42 db timeout\n" +     // 1
        "INFO req=a17 health check\n" +    // 2
        "WARN req=b42 db slow\n" +         // 3
        "INFO done a.b\n";                 // 4

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public ContextMenuTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private HighlightingViewModel? _highlighting;

    private async Task<LogTabViewModel> OpenLog(string content = Log)
    {
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, content);
        _highlighting = UiContext.None(() => new HighlightingViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None)
                .Open<HighlightPresetsFile>(HighlightPresetsFile.FileName)));
        var main = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName))
        {
            Highlighting = _highlighting,
        });
        _owned.Add(main);
        await main.OpenFileAsync(path);
        return main.SelectedTab!;
    }

    private static void Select(LogTabViewModel tab, FilePosition from, FilePosition to)
    {
        tab.Selection.MoveCaret(from, extend: false);
        tab.Selection.MoveCaret(to, extend: true);
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

    [Fact]
    public async Task Copy_to_search_puts_the_selected_text_in_the_find_box_and_runs_find_all()
    {
        var tab = await OpenLog();
        Select(tab, new FilePosition(1, 6), new FilePosition(1, 13)); // "req=b42"

        await tab.CopyToSearchCommand.ExecuteAsync(null);

        Assert.Equal("req=b42", tab.Search.FindText);
        Assert.True(tab.Search.IsFindBarOpen);
        Assert.Equal([1L, 3L], Shown(tab.Search));
    }

    [Fact]
    public async Task A_multi_line_selection_searches_for_its_part_of_the_first_line()
    {
        var tab = await OpenLog();
        Select(tab, new FilePosition(3, 4), new FilePosition(1, 14)); // dragged upwards: "db timeout" + 2 more lines

        await tab.CopyToSearchCommand.ExecuteAsync(null);

        Assert.Equal("db timeout", tab.Search.FindText);
        Assert.Equal([1L], Shown(tab.Search));
    }

    [Fact]
    public async Task With_regex_on_the_picked_text_is_escaped_so_it_still_matches_literally()
    {
        var tab = await OpenLog("x a.b y\nx acb y\n");
        tab.Search.IsRegex = true;
        Select(tab, new FilePosition(0, 2), new FilePosition(0, 5)); // "a.b"

        await tab.CopyToSearchCommand.ExecuteAsync(null);

        Assert.Equal(@"a\.b", tab.Search.FindText);
        Assert.Equal([0L], Shown(tab.Search));
    }

    [Fact]
    public async Task In_the_results_pane_it_uses_the_results_selection()
    {
        var tab = await OpenLog();
        tab.Search.AddFilterRule(new QueryRule("INFO"));
        Shown(tab.Search);
        tab.Search.ResultsSelection.MoveCaret(new FilePosition(4, 7), extend: false); // on "done"

        await tab.CopyToSearchCommand.ExecuteAsync(tab.Search.ResultsSelection);

        Assert.Equal("done", tab.Search.FindText);
        Assert.Equal([4L], Shown(tab.Search));
    }

    [Fact]
    public async Task Add_as_include_then_exclude_filter_creates_plain_text_rules_and_updates_the_results()
    {
        var tab = await OpenLog("x a.b y\nx acb y\nz a.b\n");
        Select(tab, new FilePosition(0, 2), new FilePosition(0, 5)); // "a.b": plain text, not a regex
        await tab.AddIncludeFilterCommand.ExecuteAsync(null);
        Assert.Equal([0L, 2L], Shown(tab.Search));

        tab.Navigation.Caret = new FilePosition(2, 0); // the word "z", no selection
        await tab.AddExcludeFilterCommand.ExecuteAsync(null);

        Assert.Equal([new QueryRule("a.b"), new QueryRule("z", RuleKind.Exclude)], tab.Search.GetFilterRules());
        Assert.True(tab.Search.IsFilterBarOpen);
        Assert.Equal([0L], Shown(tab.Search));
    }

    [Fact]
    public async Task Add_as_highlight_rule_adds_a_plain_text_rule_at_the_top_of_the_active_preset()
    {
        var tab = await OpenLog();
        var before = _highlighting!.Rules.Count;
        Select(tab, new FilePosition(1, 14), new FilePosition(1, 24)); // "db timeout"

        await tab.AddHighlightRuleCommand.ExecuteAsync(null);

        Assert.Equal(before + 1, _highlighting.Rules.Count);
        var rule = _highlighting.Rules[0];
        Assert.Equal("db timeout", rule.Pattern);
        Assert.False(rule.IsRegex);
        Assert.NotNull(_highlighting.Highlighter.Resolve("x db timeout y"));
    }

    [Theory]
    [InlineData(1, 2, "ERROR")]    // inside a word
    [InlineData(1, 0, "ERROR")]    // at its start
    [InlineData(1, 5, "ERROR")]    // just after its end (between "ERROR" and the space)
    [InlineData(1, 11, "b42")]     // '=' splits words
    [InlineData(4, 11, "a")]       // so does '.'
    [InlineData(3, 20, "slow")]    // the end of the line
    public async Task Without_a_selection_it_searches_for_the_word_under_the_cursor(int line, int column, string word)
    {
        var tab = await OpenLog();
        tab.Navigation.Caret = new FilePosition(line, column);

        await tab.CopyToSearchCommand.ExecuteAsync(null);

        Assert.Equal(word, tab.Search.FindText);
    }

    [Fact]
    public async Task Without_a_selection_or_a_word_under_the_cursor_nothing_happens()
    {
        var tab = await OpenLog("a  b\n");
        tab.Search.FindText = "previous";
        tab.Navigation.Caret = new FilePosition(0, 2); // between the two spaces

        await tab.CopyToSearchCommand.ExecuteAsync(null);

        Assert.Equal("previous", tab.Search.FindText);
        Assert.Null(tab.Search.Results);
    }
}
