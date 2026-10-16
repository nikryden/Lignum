using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Viewer;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>
/// The main window: one open <see cref="LogDocument"/>, its name and its live line count.
/// Document events arrive on a background thread and are marshalled to the synchronization
/// context the view model was created on (the UI thread in the app), coalesced so a fast
/// indexer cannot flood the dispatcher. Without a context (tests) updates are applied inline.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const string AppName = "Lignum";

    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private LogDocument? _document;
    private int _refreshPending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _fileName;

    [ObservableProperty]
    private long _lineCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelIndexingCommand))]
    private bool _isIndexing;

    [ObservableProperty]
    private double _indexingProgress;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>What the viewer shows: every line of the open document. Null until a file is opened.</summary>
    [ObservableProperty]
    private LineSource? _lines;

    /// <summary>Find all and the results pane for the open document (ticket 10).</summary>
    public SearchViewModel Search { get; } = new();

    public string Title => FileName is null ? AppName : $"{FileName} - {AppName}";

    /// <summary>
    /// Opens <paramref name="path"/>, replacing the current document, and indexes it in the background.
    /// The returned task completes when indexing stops; the UI does not need to await it.
    /// </summary>
    public async Task OpenFileAsync(string path)
    {
        LogDocument document;
        try
        {
            document = LogDocument.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            ErrorMessage = $"Could not open {path}: {ex.Message}";
            return;
        }

        _document?.Dispose();
        _document = document;
        ErrorMessage = null;
        FileName = document.FileName;
        document.LinesAdded += (_, _) => RequestRefresh(document);
        Lines = LineSource.ForDocument(document); // subscribes before indexing starts
        AttachChangeDetection(document);
        Search.SetDocument(document);

        var completion = document.StartIndexing();
        Refresh(document);
        await completion.ConfigureAwait(false);
        RunOnUi(() => Refresh(document));
    }

    [RelayCommand(CanExecute = nameof(IsIndexing))]
    private void CancelIndexing() => _document?.CancelIndexing();

    public void Dispose()
    {
        Search.Dispose();
        _document?.Dispose();
        _document = null;
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
        if (!ReferenceEquals(document, _document)) return; // a stale update from a replaced document

        var state = document.IndexingState;
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
