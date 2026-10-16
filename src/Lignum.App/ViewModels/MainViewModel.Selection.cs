using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Viewer;
using Lignum.App.Views;

namespace Lignum.App.ViewModels;

// Selection and copy (ticket 08): Ctrl+A selects every line, Ctrl+C copies through the Core text-range reader.
public sealed partial class MainViewModel
{
    private readonly SelectionCopier _copier;
    private CancellationTokenSource? _copyCancel;

    public MainViewModel() : this(new WpfClipboard(), new DialogCopyPrompt())
    {
    }

    public MainViewModel(IClipboardService clipboard, ICopyPrompt copyPrompt)
    {
        _copier = new SelectionCopier(clipboard, copyPrompt);
    }

    /// <summary>The main view's selection, as file positions. The viewer reads and updates it.</summary>
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
        if (_document is not { } document || Selection.IsEmpty) return;
        _copyCancel = new CancellationTokenSource();
        IsCopying = true;
        CopyMessage = "Copying…";
        try
        {
            CopyMessage = (await _copier.CopyAsync(document, Selection, _copyCancel.Token)).Message;
        }
        finally
        {
            IsCopying = false;
            _copyCancel.Dispose();
            _copyCancel = null;
        }
    }

    private bool CanCopy() => !IsCopying;

    [RelayCommand(CanExecute = nameof(IsCopying))]
    private void CancelCopy() => _copyCancel?.Cancel();

    /// <summary>A newly opened file starts with nothing selected.</summary>
    private void ResetSelection() => Selection.MoveCaret(default, extend: false);
}
