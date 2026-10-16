using System.Globalization;
using System.Windows;
using Lignum.App.ViewModels;
using Microsoft.Win32;

namespace Lignum.App.Views;

/// <summary>The Windows clipboard.</summary>
public sealed class WpfClipboard : IClipboardService
{
    public void SetText(string text) => Clipboard.SetDataObject(text, copy: true);
}

/// <summary>Asks about large copies with a message box and picks the save file with the standard dialog.</summary>
public sealed class DialogCopyPrompt : ICopyPrompt
{
    public LargeCopyChoice AskLargeCopy(long approximateBytes)
    {
        var size = (approximateBytes / (1024.0 * 1024.0)).ToString("N0", CultureInfo.CurrentCulture);
        var answer = MessageBox.Show(Application.Current?.MainWindow!,
            $"The selection is about {size} MB. Copying that much to the clipboard can take a while and use a lot of memory.\n\n" +
            "Yes: copy it anyway\nNo: save the selection to a file instead\nCancel: do nothing",
            "Large selection", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
        return answer switch
        {
            MessageBoxResult.Yes => LargeCopyChoice.Copy,
            MessageBoxResult.No => LargeCopyChoice.SaveToFile,
            _ => LargeCopyChoice.Cancel,
        };
    }

    public string? AskSavePath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save selection",
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = "selection.log",
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
}
