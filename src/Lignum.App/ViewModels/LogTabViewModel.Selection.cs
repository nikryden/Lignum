using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

// Selection and copy (ticket 08): Ctrl+A selects every line, Ctrl+C copies through the Core text-range reader.
public sealed partial class LogTabViewModel
{
    private CancellationTokenSource? _copyCancel;

    /// <summary>Copies selections; <see cref="MainViewModel"/> hands every tab its own (clipboard, prompts).</summary>
    internal SelectionCopier? Copier { get; set; }

    /// <summary>This tab's selection, as file positions. The viewer reads and updates it.</summary>
    public TextSelection Selection { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(CancelCopyCommand))]
    private bool _isCopying;

    /// <summary>The outcome of the last copy, for the status bar.</summary>
    [ObservableProperty]
    private string? _copyMessage;

    [RelayCommand]
    private void SelectAll() => Selection.SelectAll(_document?.LineCount ?? 0);

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private async Task Copy()
    {
        if (_document is not { } document || Copier is not { } copier || Selection.IsEmpty) return;
        var cancel = new CancellationTokenSource();
        _copyCancel = cancel;
        IsCopying = true;
        CopyMessage = "Copying…";
        try
        {
            CopyMessage = (await copier.CopyAsync(document, Selection, cancel.Token)).Message;
        }
        finally
        {
            IsCopying = false;
            _copyCancel = null;
            cancel.Dispose();
        }
    }

    private bool CanCopy() => !IsCopying;

    [RelayCommand(CanExecute = nameof(IsCopying))]
    private void CancelCopy() => _copyCancel?.Cancel();

    /// <summary>A newly opened or reloaded file starts with nothing selected.</summary>
    private void ResetSelection() => Selection.MoveCaret(default, extend: false);
}
