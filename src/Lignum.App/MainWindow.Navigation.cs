using System.Windows;
using System.Windows.Input;
using Lignum.App.Views;

namespace Lignum.App;

/// <summary>Go to line (Ctrl+G) and scrolling the viewer to the caret after a jump (ticket 05).</summary>
public partial class MainWindow
{
    /// <summary>Ctrl+G: opens the Go to line dialog.</summary>
    public static readonly RoutedUICommand GoToLineCommand = new("Go to line", nameof(GoToLineCommand), typeof(MainWindow));

    private void AttachNavigation()
    {
        _viewModel.Navigation.RevealRequested += (_, _) =>
        {
            LogView.RevealCaret();
            LogView.Focus();
        };
    }

    private void OnGoToLineExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel.LineCount == 0) return;
        new GoToLineDialog(_viewModel.Navigation, _viewModel.LineCount) { Owner = this }.ShowDialog();
    }
}
