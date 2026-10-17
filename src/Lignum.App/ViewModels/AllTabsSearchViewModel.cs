using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>A row of the all-tabs results: a file heading or one matching line.</summary>
public abstract class AllTabsRow(LogTabViewModel tab) : ObservableObject
{
    public LogTabViewModel Tab { get; } = tab;
}

/// <summary>The heading of one file's matches, e.g. "app.log: 12 matches"; collapses to hide them.</summary>
public sealed partial class AllTabsFileRow(LogTabViewModel tab, long matchCount, bool isCapped) : AllTabsRow(tab)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Arrow))]
    private bool _isExpanded = true;

    public long MatchCount => matchCount;

    public bool IsCapped => isCapped;

    /// <summary>This file's matching lines (shown under the heading while it is expanded).</summary>
    public List<AllTabsMatchRow> Matches { get; } = [];

    public string Arrow => IsExpanded ? "▾" : "▸";

    public string Header =>
        $"{Tab.FileName}: {(MatchCount == 1 ? "1 match" : $"{MatchCount:N0} matches")}"
        + (IsCapped ? $" (first {AllTabsSearchViewModel.MaxRowsPerFile:N0} shown)" : "");
}

/// <summary>A piece of a result line; <see cref="IsMatch"/> pieces are highlighted.</summary>
public sealed record TextSegment(string Text, bool IsMatch);

/// <summary>One matching line: its 0-based file line and (the start of) its text, split for highlighting.</summary>
public sealed class AllTabsMatchRow(LogTabViewModel tab, long line, string text, IReadOnlyList<TextSegment> segments)
    : AllTabsRow(tab)
{
    public long Line => line;

    public string Text => text;

    public IReadOnlyList<TextSegment> Segments => segments;

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
    [NotifyPropertyChangedFor(nameof(IsResultsListVisible))]
    private bool _isPaneOpen;

    /// <summary>
    /// Off (default): the matches are listed here, grouped by file. On: the search runs in each tab's own find
    /// box and results pane instead (inside that tab's filter rules, if any), and this pane lists nothing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResultsListVisible))]
    private bool _showResultsPerTab = true;

    /// <summary>Whether the main window shows the shared results list (not in per-tab mode).</summary>
    public bool IsResultsListVisible => IsPaneOpen && !ShowResultsPerTab;

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

    /// <summary>File headings and, under each expanded one, its matching lines; in tab order. Replaced on change.</summary>
    [ObservableProperty]
    private ObservableCollection<AllTabsRow> _rows = [];

    private readonly List<AllTabsFileRow> _files = [];

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
        _files.Clear();
        Rows = [];
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
        var highlight = new Query(query.Rules, SearchViewModel.HighlightRegexTimeout);
        var reads = searches.Select(s => ReadRows(s.Tab, s.Document, s.Result, highlight)).ToList();
        foreach (var read in reads)
        {
            var file = await read;
            if (generation != _generation) return; // a newer search (or Cancel) took over
            if (file is null) continue;
            _files.Add(file);
            RebuildRows();
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

    /// <summary>A click on a file heading: hides or shows that file's matching lines.</summary>
    [RelayCommand]
    private void ToggleGroup(AllTabsFileRow? file)
    {
        if (file is null) return;
        file.IsExpanded = !file.IsExpanded;
        RebuildRows();
    }

    [RelayCommand]
    private void CollapseAll() => SetAllExpanded(false);

    [RelayCommand]
    private void ExpandAll() => SetAllExpanded(true);

    private void SetAllExpanded(bool expanded)
    {
        foreach (var file in _files) file.IsExpanded = expanded;
        RebuildRows();
    }

    private void RebuildRows()
    {
        var rows = new List<AllTabsRow>();
        foreach (var file in _files)
        {
            rows.Add(file);
            if (file.IsExpanded) rows.AddRange(file.Matches);
        }

        Rows = new ObservableCollection<AllTabsRow>(rows);
    }

    [RelayCommand]
    private void ToggleMatchCase() => MatchCase = !MatchCase;

    [RelayCommand]
    private void ToggleRegex() => IsRegex = !IsRegex;

    partial void OnFindTextChanged(string value) => FindTextError = null;

    /// <summary>Waits for one file's search, then reads its matching lines' text in the background.</summary>
    private static async Task<AllTabsFileRow?> ReadRows(LogTabViewModel tab, LogDocument document, QueryResult result,
                                                        Query highlight)
    {
        await result.Completion.ConfigureAwait(false);
        if (result.State != QueryState.Completed || result.Count == 0) return null;
        return await Task.Run(() =>
        {
            var shown = (int)Math.Min(result.Count, MaxRowsPerFile);
            var file = new AllTabsFileRow(tab, result.Count, shown < result.Count);
            var rows = file.Matches;
            try
            {
                for (var i = 0; i < shown; i++)
                {
                    var line = result[i];
                    var prefix = document.ReadLinePrefixes(line, 1, MaxTextLength);
                    var text = prefix.Length > 0 ? prefix[0].Text + (prefix[0].IsTruncated ? " …" : "") : "";
                    rows.Add(new AllTabsMatchRow(tab, line, text, Split(text, highlight)));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The tab closed or the file went away meanwhile: list what was read.
            }

            return file;
        }).ConfigureAwait(false);
    }

    /// <summary>Cuts <paramref name="text"/> into plain and matched pieces (best effort: a regex timeout gives one plain piece).</summary>
    private static List<TextSegment> Split(string text, Query highlight)
    {
        var segments = new List<TextSegment>();
        var pos = 0;
        foreach (var match in highlight.FindMatches(text))
        {
            if (match.Start < pos || match.Start + match.Length > text.Length) continue;
            if (match.Start > pos) segments.Add(new TextSegment(text[pos..match.Start], false));
            segments.Add(new TextSegment(text.Substring(match.Start, match.Length), true));
            pos = match.Start + match.Length;
        }

        if (pos < text.Length) segments.Add(new TextSegment(text[pos..], false));
        return segments;
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
