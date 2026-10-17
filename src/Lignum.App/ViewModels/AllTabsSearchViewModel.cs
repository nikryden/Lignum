using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>A row of the all-tabs results: a file heading or one matching line.</summary>
public abstract record AllTabsRow(LogTabViewModel Tab);

/// <summary>The heading of one file's matches, e.g. "app.log: 12 matches".</summary>
public sealed record AllTabsFileRow(LogTabViewModel Tab, long MatchCount, bool IsCapped) : AllTabsRow(Tab)
{
    public string Header =>
        $"{Tab.FileName}: {(MatchCount == 1 ? "1 match" : $"{MatchCount:N0} matches")}"
        + (IsCapped ? $" (first {AllTabsSearchViewModel.MaxRowsPerFile:N0} shown)" : "");
}

/// <summary>One matching line: its 0-based file line and (the start of) its text.</summary>
public sealed record AllTabsMatchRow(LogTabViewModel Tab, long Line, string Text) : AllTabsRow(Tab)
{
    public long DisplayLine => Line + 1;
}

/// <summary>
/// Find in all tabs (ticket 01): one search over every open tab, in the background, with progress and
/// cancel. Results are grouped by file in the shared pane; a click switches to that tab at the line. Each
/// file's matches appear when that file is done. Line text is read in the background, cut to
/// <see cref="MaxTextLength"/> characters.
/// </summary>
public sealed partial class AllTabsSearchViewModel : ObservableObject
{
    /// <summary>At most this many matching lines are listed per file (all of them are counted).</summary>
    public const int MaxRowsPerFile = 10_000;

    private const int MaxTextLength = 500;

    private readonly SynchronizationContext? _uiContext;
    private readonly Func<IReadOnlyList<LogTabViewModel>> _tabs;
    private readonly Action<LogTabViewModel, long> _goTo;
    private List<QueryResult> _running = [];
    private int _generation;
    private int _progressPending;

    [ObservableProperty]
    private string _findText = "";

    [ObservableProperty]
    private bool _isRegex;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _useNonBacktracking;

    [ObservableProperty]
    private string? _findTextError;

    [ObservableProperty]
    private bool _isPaneOpen;

    /// <summary>
    /// Off (default): the matches are listed here, grouped by file. On: the search runs in each tab's own find
    /// box and results pane instead (inside that tab's filter rules, if any), and this pane lists nothing.
    /// </summary>
    [ObservableProperty]
    private bool _showResultsPerTab;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private string _status = "";

    /// <param name="tabs">The open tabs, read when a search starts.</param>
    /// <param name="goTo">Selects a tab and moves its caret to a 0-based line.</param>
    internal AllTabsSearchViewModel(SynchronizationContext? uiContext, Func<IReadOnlyList<LogTabViewModel>> tabs,
                                    Action<LogTabViewModel, long> goTo)
    {
        _uiContext = uiContext;
        _tabs = tabs;
        _goTo = goTo;
    }

    /// <summary>File headings and matching lines, grouped by file in tab order.</summary>
    public ObservableCollection<AllTabsRow> Rows { get; } = [];

    /// <summary>Raised when the find box should take the keyboard.</summary>
    public event EventHandler? FindBoxFocusRequested;

    /// <summary>Ctrl+Shift+F: opens the pane and focuses its find box.</summary>
    [RelayCommand]
    private void Open()
    {
        IsPaneOpen = true;
        FindBoxFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        Cancel();
        IsPaneOpen = false;
    }

    /// <summary>Searches every open tab for <see cref="FindText"/>; completes when all of them are done.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Search()
    {
        Cancel();
        Rows.Clear();
        Status = "";
        if (string.IsNullOrEmpty(FindText)) return;

        Query query;
        try
        {
            query = Query.Find(FindText, MatchCase, IsRegex, UseNonBacktracking);
        }
        catch (ArgumentException ex)
        {
            FindTextError = ex.Message;
            return;
        }

        FindTextError = null;
        if (ShowResultsPerTab)
        {
            await SearchEachTab();
            return;
        }

        var generation = ++_generation;
        var searches = new List<(LogTabViewModel Tab, LogDocument Document, QueryResult Result)>();
        foreach (var tab in _tabs())
        {
            if (tab.Document is not { } document) continue;
            var result = document.StartQuery(query);
            result.Changed += (_, _) => RequestProgress(generation);
            searches.Add((tab, document, result));
        }

        _running = [.. searches.Select(s => s.Result)];
        IsSearching = searches.Count > 0;
        UpdateProgress(generation);

        // Each file's rows go in as it finishes, but always in tab order (a finished file waits for the ones before it).
        var reads = searches.Select(s => ReadRows(s.Tab, s.Document, s.Result)).ToList();
        foreach (var read in reads)
        {
            var rows = await read;
            if (generation != _generation) return; // a newer search (or Cancel) took over
            foreach (var row in rows) Rows.Add(row);
        }

        IsSearching = false;
        UpdateProgress(generation);
    }

    /// <summary>The per-tab mode: the same search in every tab's own find box; completes when all are done.</summary>
    private async Task SearchEachTab()
    {
        var tabs = _tabs();
        var generation = ++_generation;
        IsSearching = tabs.Count > 0;
        Status = $"Searching {tabs.Count} tabs…";
        var searches = new List<Task>();
        foreach (var tab in tabs)
        {
            var search = tab.Search;
            search.IsRegex = IsRegex;
            search.MatchCase = MatchCase;
            search.UseNonBacktracking = UseNonBacktracking;
            search.FindText = FindText;
            search.IsFindBarOpen = true;
            searches.Add(search.FindAllCommand.ExecuteAsync(null));
        }

        await Task.WhenAll(searches);
        if (generation != _generation) return;
        IsSearching = false;
        var matches = tabs.Sum(t => t.Search.MatchCount);
        Status = $"{(matches == 1 ? "1 match" : $"{matches:N0} matches")} in {tabs.Count} tabs: see each tab's results pane";
    }

    [RelayCommand(CanExecute = nameof(IsSearching))]
    private void Cancel()
    {
        _generation++;
        foreach (var result in _running) result.Cancel();
        if (ShowResultsPerTab && IsSearching)
        {
            foreach (var tab in _tabs()) tab.Search.CancelSearchCommand.Execute(null);
        }

        _running = [];
        if (IsSearching)
        {
            IsSearching = false;
            Status = "Search cancelled";
        }
    }

    /// <summary>A click on a row: switches to its tab at its line (a heading goes to the tab only).</summary>
    [RelayCommand]
    private void GoTo(AllTabsRow? row)
    {
        if (row is null || !_tabs().Contains(row.Tab)) return; // the tab was closed since
        _goTo(row.Tab, row is AllTabsMatchRow match ? match.Line : -1);
    }

    [RelayCommand]
    private void ToggleMatchCase() => MatchCase = !MatchCase;

    [RelayCommand]
    private void ToggleRegex() => IsRegex = !IsRegex;

    partial void OnFindTextChanged(string value) => FindTextError = null;

    /// <summary>Waits for one file's search, then reads its matching lines' text in the background.</summary>
    private static async Task<List<AllTabsRow>> ReadRows(LogTabViewModel tab, LogDocument document, QueryResult result)
    {
        await result.Completion.ConfigureAwait(false);
        if (result.State != QueryState.Completed || result.Count == 0) return [];
        return await Task.Run(() =>
        {
            var shown = (int)Math.Min(result.Count, MaxRowsPerFile);
            var rows = new List<AllTabsRow>(shown + 1) { new AllTabsFileRow(tab, result.Count, shown < result.Count) };
            try
            {
                for (var i = 0; i < shown; i++)
                {
                    var line = result[i];
                    var prefix = document.ReadLinePrefixes(line, 1, MaxTextLength);
                    var text = prefix.Length > 0 ? prefix[0].Text + (prefix[0].IsTruncated ? " …" : "") : "";
                    rows.Add(new AllTabsMatchRow(tab, line, text));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The tab closed or the file went away meanwhile: list what was read.
            }

            return rows;
        }).ConfigureAwait(false);
    }

    private void RequestProgress(int generation)
    {
        if (_uiContext is null)
        {
            UpdateProgress(generation);
            return;
        }

        if (Interlocked.Exchange(ref _progressPending, 1) == 0)
        {
            _uiContext.Post(_ =>
            {
                Volatile.Write(ref _progressPending, 0);
                UpdateProgress(generation);
            }, null);
        }
    }

    private void UpdateProgress(int generation)
    {
        if (generation != _generation) return;
        var results = _running;
        var files = results.Count;
        var matches = results.Sum(r => r.Count);
        var matchText = matches == 1 ? "1 match" : $"{matches:N0} matches";
        var fileText = files == 1 ? "1 file" : $"{files} files";
        var failed = results.FirstOrDefault(r => r.State == QueryState.Failed)?.Error;
        Status = failed is not null ? $"Search failed: {failed.Message}"
            : files == 0 ? "No open files"
            : IsSearching ? $"Searching {fileText}… {results.Average(r => r.Progress):P0} ({matchText})"
            : $"{matchText} in {fileText}";
    }
}
