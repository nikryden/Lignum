using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.Core;

namespace Lignum.App.ViewModels;

// The "File changed: Reload" banner, and the change notifications auto update (AutoUpdate partial) acts on.
public sealed partial class LogTabViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyPropertyChangedFor(nameof(Indicator))]
    private bool _isFileChanged;

    [ObservableProperty]
    private string _fileChangeMessage = "";

    /// <summary>
    /// Brings the tab up to date once the file has changed: a grown file has only its new bytes indexed, a
    /// truncated or replaced one is reopened and indexed again in the same tab. Completes when that stops.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsFileChanged))]
    private Task Reload() => Update();

    /// <summary>
    /// Opens this tab's path again on a background thread and disposes the current document. Null when the tab
    /// was closed meanwhile, or the file could not be opened (the current document then stays).
    /// </summary>
    private async Task<LogDocument?> OpenAgainAsync()
    {
        if (_document is null) return null; // closed

        LogDocument document;
        try
        {
            document = await LogDocument.OpenAsync(Path, EncodingOverride);
        }
        catch (Exception ex) when (LogDocument.IsOpenFailure(ex))
        {
            ErrorMessage = $"Could not reload {Path}: {ex.Message}";
            return null;
        }

        if (_document is null) // closed while opening
        {
            document.Dispose();
            return null;
        }

        _document.Dispose();
        return document;
    }

    /// <summary>Called for each newly opened document, before indexing starts.</summary>
    private void AttachChangeDetection(LogDocument document)
    {
        IsFileChanged = false;
        FileChangeMessage = "";
        _positionAtChange = null;
        document.FileChanged += (_, e) => RunOnUi(() => OnFileChanged(document, e));
    }

    private void OnFileChanged(LogDocument document, FileChangedEventArgs e)
    {
        if (!ReferenceEquals(document, _document)) return; // a late event from a replaced document

        // A truncated/replaced file's index is gone: refresh so the stale lines disappear at once. Remember
        // where the view was first: with no lines left, "is the last line in view" would always say yes.
        if (e.InvalidatesIndex)
        {
            _positionAtChange ??= (ScrollLine, IsLastLineInView);
            Refresh(document);
        }

        if (AutoUpdate)
        {
            _ = Update();
            return;
        }

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
