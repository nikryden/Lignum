using Lignum.App.Packaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>
/// One open log in one tab: it owns its <see cref="LogDocument"/> and everything that belongs to that
/// file alone (indexing state, the line source the viewer shows, the file-changed banner; later the
/// find/results pane, filters, bookmarks and scroll position). App-wide state (the tab list, recent
/// files, appearance) lives in <see cref="MainViewModel"/>.
/// Document events arrive on a background thread and are marshalled to the synchronization context
/// handed in by <see cref="MainViewModel"/> (the UI thread in the app), coalesced so a fast indexer cannot
/// flood the dispatcher. Without a context (tests) updates are applied inline.
/// </summary>
public sealed partial class LogTabViewModel : ObservableObject, IDisposable
{
    private readonly SynchronizationContext? _uiContext;
    private LogDocument? _document;
    private int _refreshPending;

    [ObservableProperty]
    private long _lineCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelIndexingCommand))]
    private bool _isIndexing;

    [ObservableProperty]
    private double _indexingProgress;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>A problem with this tab's file after it was opened (e.g. a failed reload).</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>What the viewer shows: every line of the document.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewLines))]
    private LineSource? _lines;

    /// <summary>True while this is the tab shown in the window (kept in sync by <see cref="MainViewModel"/>).</summary>
    [ObservableProperty]
    private bool _isSelected;

    private readonly TabServices _services;

    /// <summary>Takes ownership of an opened (not yet indexing) <paramref name="document"/>; call <see cref="Start"/>.</summary>
    internal LogTabViewModel(LogDocument document, SynchronizationContext? uiContext, TabServices services)
    {
        _uiContext = uiContext;
        _services = services;
        Path = document.Path;
        FileName = document.FileName;
        _document = document;
    }

    /// <summary>The full path of the file being read (for a package, the extracted log).</summary>
    public string Path { get; }

    /// <summary>The <c>.lm</c> package this tab shows (ticket 06), or null for a plain log file.</summary>
    internal OpenedPackage? Package { get; init; }

    /// <summary>What the user opened: the package for a package tab, else <see cref="Path"/>. Identifies the tab.</summary>
    public string SourcePath => Package?.PackagePath ?? Path;

    public string FileName { get; }

    /// <summary>Completes when the current document's indexing stops (replaced by a reload).</summary>
    public Task IndexingCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>Starts indexing the document passed to the constructor.</summary>
    internal Task Start() => Attach(_document!);

    [RelayCommand(CanExecute = nameof(IsIndexing))]
    private void CancelIndexing() => _document?.CancelIndexing();

    /// <summary>Closes the file: the handle is released once the indexer has stopped.</summary>
    public void Dispose()
    {
        _search?.Dispose();
        _document?.Dispose();
        _document = null;
        if (Package is { } package)
        {
            // Once the indexer has let go of the file, remove the extracted copy (and its bookmarks).
            _ = IndexingCompletion.ContinueWith(_ =>
            {
                Settings.SettingsStore.Flush();
                LogPackage.DeleteExtracted(package.Folder);
            }, TaskScheduler.Default);
        }
    }

    private Task Attach(LogDocument document)
    {
        _document = document;
        ErrorMessage = null;
        ResetSelection();
        document.LinesAdded += (_, _) => RequestRefresh(document);
        Lines = LineSource.ForDocument(document); // subscribes before indexing starts
        AttachChangeDetection(document);
        AttachNavigation(document);
        Search.SetDocument(document);

        var completion = document.StartIndexing();
        Refresh(document);
        IndexingCompletion = FinishIndexing(document, completion);
        return IndexingCompletion;
    }

    private async Task FinishIndexing(LogDocument document, Task completion)
    {
        await completion.ConfigureAwait(false);
        RunOnUi(() => Refresh(document));
    }

    private void RequestRefresh(LogDocument document)
    {
        if (_uiContext is null)
        {
            Refresh(document);
            return;
        }

        if (Interlocked.Exchange(ref _refreshPending, 1) == 0)
        {
            _uiContext.Post(_ =>
            {
                Volatile.Write(ref _refreshPending, 0);
                Refresh(document);
            }, null);
        }
    }

    private void RunOnUi(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }

    private void Refresh(LogDocument document)
    {
        if (!ReferenceEquals(document, _document)) return; // a stale update from a replaced or closed document

        var state = document.IndexingState;
        RefreshEncoding(document);
        LineCount = document.LineCount;
        IndexingProgress = document.IndexingProgress;
        IsIndexing = state is IndexingState.Running or IndexingState.NotStarted;
        StatusText = state switch
        {
            IndexingState.Running or IndexingState.NotStarted => $"Indexing… {document.IndexingProgress:P0}",
            IndexingState.Cancelled => "Indexing cancelled",
            IndexingState.Failed => $"Indexing failed: {document.IndexingError?.Message}",
            _ => "",
        };
    }
}
