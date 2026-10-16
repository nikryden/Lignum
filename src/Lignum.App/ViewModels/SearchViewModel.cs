using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>
/// Find all for one document: the find box text, the running query and its result set (shown in the
/// results pane), the match count / progress for the status bar, and F3 / Shift+F3 navigation.
/// Query updates arrive on background threads and are coalesced onto the context the view model was
/// created on (inline without one).
/// </summary>
/// <remarks>
/// Ticket 11 adds regex / case toggles (build the <see cref="Query"/> in <see cref="BuildQuery"/>);
/// ticket 12 feeds filter rules into the same result set and pane.
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    private readonly SynchronizationContext? _uiContext;
    private LogDocument? _document;
    private QueryResult? _result;
    private int _refreshPending;

    [ObservableProperty]
    private string _findText = "";

    [ObservableProperty]
    private bool _isFindBarOpen;

    [ObservableProperty]
    private bool _isResultsPaneOpen;

    /// <summary>The result set as a line source for the results pane. Null before a search.</summary>
    [ObservableProperty]
    private LineSource? _results;

    /// <summary>The query whose matches the views highlight. Null when nothing is searched.</summary>
    [ObservableProperty]
    private Query? _highlight;

    [ObservableProperty]
    private long _matchCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelSearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private double _searchProgress;

    [ObservableProperty]
    private string _searchStatus = "";

    /// <summary>The file line the user last went to (a result, F3, or set by the view); F3 continues from here.</summary>
    [ObservableProperty]
    private long? _currentLine;

    /// <summary>The results row of <see cref="CurrentLine"/> when the user went there through the results.</summary>
    [ObservableProperty]
    private long? _currentResultRow;

    /// <param name="uiContext">Where query updates are applied; null applies them inline (tests).</param>
    internal SearchViewModel(SynchronizationContext? uiContext) => _uiContext = uiContext;

    /// <summary>Raised when the find box should take the keyboard (Ctrl+F, even if it is already open).</summary>
    public event EventHandler? FindBoxFocusRequested;

    /// <summary>Raised with a 0-based file line when the main view should scroll to it and select it.</summary>
    public event EventHandler<long>? NavigateRequested;

    /// <summary>Switches to another document (or none): stops the search and clears the results.</summary>
    public void SetDocument(LogDocument? document)
    {
        StopSearch();
        _document = document;
        Results = null;
        Highlight = null;
        MatchCount = 0;
        SearchProgress = 0;
        SearchStatus = "";
        CurrentLine = null;
        CurrentResultRow = null;
        IsSearching = false;
    }

    /// <summary>Runs find all for <see cref="FindText"/>; completes when the search stops.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task FindAll()
    {
        if (_document is not { } document || string.IsNullOrEmpty(FindText)) return;

        StopSearch();
        var query = BuildQuery();
        var result = document.StartQuery(query);
        _result = result;
        Results = new LineSource(new ResultLines(result), new DocumentLineReader(document));
        Highlight = query;
        CurrentLine = null;
        CurrentResultRow = null;
        IsResultsPaneOpen = true;
        IsSearching = true;
        result.Changed += (_, _) => RequestRefresh(result);
        Refresh(result);

        await result.Completion.ConfigureAwait(false);
        RunOnUi(() => Refresh(result));
    }

    [RelayCommand(CanExecute = nameof(IsSearching))]
    private void CancelSearch() => _result?.Cancel();

    [RelayCommand]
    private void OpenFindBar()
    {
        IsFindBarOpen = true;
        FindBoxFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CloseFindBar() => IsFindBarOpen = false;

    [RelayCommand]
    private void ToggleResultsPane() => IsResultsPaneOpen = !IsResultsPaneOpen;

    /// <summary>F3: the first match after <see cref="CurrentLine"/>, wrapping to the first.</summary>
    [RelayCommand]
    private void FindNext()
    {
        if (_result is not { Count: > 0 } result) return;
        var row = CurrentLine is { } line ? result.FindRow(line + 1) : 0;
        if (row >= result.Count) row = 0;
        GoToResult(row);
    }

    /// <summary>Shift+F3: the last match before <see cref="CurrentLine"/>, wrapping to the last.</summary>
    [RelayCommand]
    private void FindPrevious()
    {
        if (_result is not { Count: > 0 } result) return;
        var row = CurrentLine is { } line ? result.FindRow(line) - 1 : result.Count - 1;
        if (row < 0) row = result.Count - 1;
        GoToResult(row);
    }

    /// <summary>Goes to the line shown in results row <paramref name="row"/> (a click in the results pane).</summary>
    public void GoToResult(long row)
    {
        if (_result is not { } result || row < 0 || row >= result.Count) return;
        var line = result[row];
        CurrentLine = line;
        CurrentResultRow = row;
        NavigateRequested?.Invoke(this, line);
    }

    public void Dispose() => StopSearch();

    private Query BuildQuery() => Query.Find(FindText);

    private void StopSearch()
    {
        _result?.Cancel();
        _result = null;
    }

    private void RequestRefresh(QueryResult result)
    {
        if (_uiContext is null)
        {
            Refresh(result);
            return;
        }

        if (Interlocked.Exchange(ref _refreshPending, 1) == 0)
        {
            _uiContext.Post(_ =>
            {
                Volatile.Write(ref _refreshPending, 0);
                Refresh(result);
            }, null);
        }
    }

    private void RunOnUi(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }

    private void Refresh(QueryResult result)
    {
        if (!ReferenceEquals(result, _result)) return; // a stale update from a replaced search

        var count = result.Count;
        var state = result.State;
        MatchCount = count;
        SearchProgress = result.Progress;
        IsSearching = state == QueryState.Running;
        var matches = count == 1 ? "1 match" : $"{count:N0} matches";
        SearchStatus = state switch
        {
            QueryState.Running => $"Searching… {result.Progress:P0} ({matches})",
            QueryState.Cancelled => $"Search cancelled ({matches})",
            QueryState.Failed => $"Search failed: {result.Error?.Message}",
            _ => matches,
        };
    }
}
