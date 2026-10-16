using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.Core;

namespace Lignum.App.ViewModels;

// The "File changed: Reload" banner. Kept apart from the rest of the view model so tail mode (v1.1) can
// hook the same Grown notifications here without touching document opening.
public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private bool _isFileChanged;

    [ObservableProperty]
    private string _fileChangeMessage = "";

    /// <summary>Reopens the current path and indexes it again. Available once the file has changed.</summary>
    [RelayCommand(CanExecute = nameof(IsFileChanged))]
    private Task Reload() => _document is { } document ? OpenFileAsync(document.Path) : Task.CompletedTask;

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
