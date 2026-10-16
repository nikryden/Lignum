using Lignum.Core;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

/// <summary>
/// The caret of one open document and the commands that move it: go to line (Ctrl+G), start and end of
/// file (Ctrl+Home / Ctrl+End). The caret is a <see cref="FilePosition"/>; the view shows and scrolls to it.
/// </summary>
public sealed partial class NavigationViewModel(Func<long> lineCount) : ObservableObject
{
    /// <summary>Where the caret is. The view sets it when the user clicks or uses the arrow keys.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaretText))]
    private FilePosition _caret;

    /// <summary>The caret for the status bar, 1-based: "Ln 42, Col 10".</summary>
    public string CaretText => string.Create(CultureInfo.CurrentCulture,
        $"Ln {Caret.Line + 1:N0}, Col {Caret.Column + 1:N0}");

    /// <summary>
    /// Raised after every jump (go to line, start, end), even one that leaves the caret where it was, so the
    /// view scrolls the caret into sight.
    /// </summary>
    public event EventHandler? RevealRequested;

    /// <summary>Ctrl+Home: the start of the file.</summary>
    [RelayCommand]
    private void GoToStart() => JumpTo(FilePosition.Start);

    /// <summary>Ctrl+End: the start of the last line indexed so far.</summary>
    [RelayCommand]
    private void GoToEnd() => JumpTo(new FilePosition(Math.Max(0, lineCount() - 1), 0));

    /// <summary>A new document: back to the start.</summary>
    public void Reset() => Caret = FilePosition.Start;

    /// <summary>Moves the caret to the start of the 1-based line typed by the user.</summary>
    /// <returns>False, with a message for the user in <paramref name="error"/>, if the input is not a line of the file.</returns>
    public bool TryGoToLine(string? input, out string? error)
    {
        var count = lineCount();
        var text = input?.Trim() ?? "";
        if (!IsWholeNumber(text))
        {
            error = "Enter a line number.";
            return false;
        }

        if (count <= 0)
        {
            error = "There are no lines to go to.";
            return false;
        }

        // Too many digits for a long is simply out of range.
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var line)
            || line < 1 || line > count)
        {
            error = $"Enter a line between 1 and {count.ToString("N0", CultureInfo.CurrentCulture)}.";
            return false;
        }

        error = null;
        JumpTo(new FilePosition(line - 1, 0));
        return true;
    }

    private void JumpTo(FilePosition position)
    {
        Caret = position;
        RevealRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsWholeNumber(string text)
    {
        var digits = text.StartsWith('-') || text.StartsWith('+') ? text.AsSpan(1) : text.AsSpan();
        return digits.Length > 0 && !digits.ContainsAnyExceptInRange('0', '9');
    }
}
