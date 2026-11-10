using System.Windows.Input;
using Lignum.App.Views;

namespace Lignum.App;

/// <summary>Go to line (Ctrl+G) for the selected tab (ticket 05). Scrolling to the caret is done by LogTabView.</summary>
public partial class MainWindow
{
    /// <summary>Ctrl+G: opens the Go to line dialog.</summary>
    public static readonly RoutedUICommand GoToLineCommand = new("Go to line", nameof(GoToLineCommand), typeof(MainWindow));

    public static readonly RoutedUICommand OpenFolderCommand = new("Open folder", nameof(OpenFolderCommand), typeof(MainWindow));

    private void OnGoToLineExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel.SelectedTab is not { LineCount: > 0 } tab) return;
        new GoToLineDialog(tab.Navigation, tab.LineCount) { Owner = DialogOwner }.ShowDialog();
    }
}
