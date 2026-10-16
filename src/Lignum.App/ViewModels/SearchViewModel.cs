using System.Text.RegularExpressions;
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
/// The regex / case / NonBacktracking toggles shape the query in <see cref="BuildQuery"/>; an invalid
/// pattern sets <see cref="FindTextError"/> instead of searching. Filter rules (SearchViewModel.Filter.cs)
/// feed the same result set and pane: whichever of find all and the filter ran last owns it.
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    private readonly SynchronizationContext? _uiContext;
    private LogDocument? _document;
    private QueryResult? _result;
    private int _refreshPending;
    private bool _showingFilter;

    [ObservableProperty]
    private string _findText = "";

    /// <summary>Treat <see cref="FindText"/> as a .NET regular expression.</summary>
    [ObservableProperty]
    private bool _isRegex;

    /// <summary>Case-sensitive matching (plain text and regex).</summary>
    [ObservableProperty]
    private bool _matchCase;

    /// <summary>Run regexes on the linear-time NonBacktracking engine (no backreferences or lookarounds).</summary>
    [ObservableProperty]
    private bool _useNonBacktracking;

    /// <summary>Why <see cref="FindText"/> cannot be searched (an invalid regex); null when it is fine.</summary>
    [ObservableProperty]
    private string? _findTextError;

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

    /// <summary>Where searched terms and filter rules are remembered (the dropdowns); null records nothing.</summary>
    public SearchHistory? History { get; init; }

    /// <summary>
    /// Regex timeout for highlighting: the views run it per visible row on the UI thread, so it must stay
    /// short. A timeout there only drops highlights.
    /// </summary>
    internal static readonly TimeSpan HighlightRegexTimeout = TimeSpan.FromMilliseconds(50);

    /// <summary>Limit for one regex match on one line during a search (tests shorten it).</summary>
    internal TimeSpan RegexTimeout { get; set; } = Query.DefaultRegexTimeout;

    /// <summary>Raised when the find box should take the keyboard (Ctrl+F, even if it is already open).</summary>
    public event EventHandler? FindBoxFocusRequested;

    /// <summary>Raised with a 0-based file line when the main view should scroll to it and select it.</summary>
    public event EventHandler<long>? NavigateRequested;

    /// <summary>Switches to another document (or none): stops the search, clears the results and re-applies the filter rules.</summary>
    public void SetDocument(LogDocument? document)
    {
        _document = document;
        ClearResults();
        ApplyFilter(); // filter rules belong to the tab, not the document: run them on the new one
    }

    /// <summary>Runs find all for <see cref="FindText"/>; completes when the search stops.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task FindAll()
    {
        if (_document is not { } document || string.IsNullOrEmpty(FindText)) return;

        Query query;
        try
        {
            query = BuildQuery(RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            FindTextError = ex.Message; // invalid pattern: keep whatever was shown, start nothing
            return;
        }

        FindTextError = null;
        History?.AddSearchTerm(FindText);
        await RunQuery(document, query, isFilter: false);
    }

    /// <summary>
    /// Shows <paramref name="query"/>'s results in the results pane (find all and the filter share it: the
    /// last one run wins), cancelling the query shown before. Completes when the query stops.
    /// </summary>
    private async Task RunQuery(LogDocument document, Query query, bool isFilter)
    {
        StopSearch();
        var result = document.StartQuery(query);
        _result = result;
        _showingFilter = isFilter;
        Results = new LineSource(new ResultLines(result), new DocumentLineReader(document));
        ResultsSelection.MoveCaret(default, extend: false);
        Highlight = new Query(query.Rules, HighlightRegexTimeout);
        CurrentLine = null;
        CurrentResultRow = null;
        IsResultsPaneOpen = true;
        IsSearching = true;
        result.Changed += (_, _) => RequestRefresh(result);
        Refresh(result);

        await result.Completion.ConfigureAwait(false);
        RunOnUi(() => Refresh(result));
    }

    /// <summary>Clears the results pane's content (not its open state).</summary>
    private void ClearResults()
    {
        StopSearch();
        _showingFilter = false;
        Results = null;
        Highlight = null;
        MatchCount = 0;
        SearchProgress = 0;
        SearchStatus = "";
        CurrentLine = null;
        CurrentResultRow = null;
        IsSearching = false;
        ResultsSelection.MoveCaret(default, extend: false);
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

    [RelayCommand]
    private void ToggleMatchCase() => MatchCase = !MatchCase;

    [RelayCommand]
    private void ToggleRegex() => IsRegex = !IsRegex;

    [RelayCommand]
    private void ToggleNonBacktracking() => UseNonBacktracking = !UseNonBacktracking;

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

    /// <summary>Throws <see cref="ArgumentException"/> (with a user-facing message) for an invalid pattern.</summary>
    private Query BuildQuery(TimeSpan regexTimeout) =>
        new([new QueryRule(FindText, RuleKind.Include, IsRegex, MatchCase, NonBacktracking: UseNonBacktracking)],
            regexTimeout);

    partial void OnFindTextChanged(string value) => FindTextError = null;

    partial void OnIsRegexChanged(bool value) => OnOptionChanged();

    partial void OnMatchCaseChanged(bool value) => OnOptionChanged();

    partial void OnUseNonBacktrackingChanged(bool value) => OnOptionChanged();

    /// <summary>A toggle changed: drop a stale error and re-run the search the results pane shows.</summary>
    private void OnOptionChanged()
    {
        FindTextError = null;
        if (Results is not null && !_showingFilter && !string.IsNullOrEmpty(FindText)) FindAllCommand.Execute(null);
    }

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
        if (result.Error is RegexMatchTimeoutException)
        {
            // Don't keep a pattern that blows up as the highlight: the views would run it on every row.
            Highlight = null;
            SearchStatus = $"Search stopped: the regex took too long on one line (over {RegexTimeout.TotalSeconds:0.#} s). "
                + "Simplify the pattern or turn on NonBacktracking.";
            return;
        }

        if (_showingFilter)
        {
            var lines = count == 1 ? "1 line" : $"{count:N0} lines";
            SearchStatus = state switch
            {
                QueryState.Running => $"Filtering… {result.Progress:P0} ({lines})",
                QueryState.Cancelled => $"Filter cancelled ({lines})",
                QueryState.Failed => $"Filter failed: {result.Error?.Message}",
                _ => $"Filter: {lines}",
            };
            return;
        }

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
