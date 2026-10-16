using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.Core;

namespace Lignum.App.ViewModels;

// The "File changed: Reload" banner. Kept apart from the rest of the view model so tail mode (v1.1) can
// hook the same Grown notifications here without touching document opening.
public sealed partial class LogTabViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private bool _isFileChanged;

    [ObservableProperty]
    private string _fileChangeMessage = "";

    /// <summary>
    /// Reopens this tab's path and indexes it again, in the same tab. Available once the file has changed.
    /// Completes when the new indexing stops.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsFileChanged))]
    private Task Reload() => Reopen();

    /// <summary>
    /// Opens this tab's path again (with the chosen encoding, if any) and indexes it in the same tab. On
    /// failure the current document stays and <see cref="ErrorMessage"/> says why.
    /// </summary>
    private Task Reopen()
    {
        if (_document is null) return Task.CompletedTask; // closed

        LogDocument document;
        try
        {
            document = LogDocument.Open(Path, EncodingOverride);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            ErrorMessage = $"Could not reload {Path}: {ex.Message}";
            return Task.CompletedTask;
        }

        _document.Dispose();
        return Attach(document);
    }

    /// <summary>Called for each newly opened document, before indexing starts.</summary>
    private void AttachChangeDetection(LogDocument document)
    {
        IsFileChanged = false;
        FileChangeMessage = "";
        document.FileChanged += (_, e) => RunOnUi(() => OnFileChanged(document, e));
    }

    private void OnFileChanged(LogDocument document, FileChangedEventArgs e)
    {
        if (!ReferenceEquals(document, _document)) return; // a late event from a replaced document

        // A truncated/replaced file's index is gone: refresh so the stale lines disappear at once.
        if (e.InvalidatesIndex) Refresh(document);

        // Growth after an invalidating change must not soften the message.
        if (IsFileChanged && !e.InvalidatesIndex) return;
        FileChangeMessage = e.Kind switch
        {
            FileChangeKind.Truncated => "The file was truncated on disk.",
            FileChangeKind.Replaced => "The file was replaced or removed on disk.",
            _ => "The file has changed on disk.",
        };
        IsFileChanged = true;
    }
}
