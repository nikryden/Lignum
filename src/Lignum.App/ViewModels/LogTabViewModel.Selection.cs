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

    private TextSelection? _selection;

    /// <summary>
    /// This tab's selection, as file positions. The viewer reads and updates it. Its caret is the tab's caret
    /// (<see cref="NavigationViewModel.Caret"/>): they are kept equal, and a caret move that did not come from
    /// the selection (plain arrows, a click without Shift, go to line) clears it.
    /// </summary>
    public TextSelection Selection => _selection ??= CreateSelection();

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

    private TextSelection CreateSelection()
    {
        var selection = new TextSelection();
        var navigation = Navigation;
        selection.Changed += (_, _) => navigation.Caret = selection.Caret;
        navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigationViewModel.Caret) && navigation.Caret != selection.Caret)
                selection.MoveCaret(navigation.Caret, extend: false);
        };
        return selection;
    }

    /// <summary>A newly opened or reloaded file starts with nothing selected.</summary>
    private void ResetSelection() => Selection.MoveCaret(default, extend: false);
}
