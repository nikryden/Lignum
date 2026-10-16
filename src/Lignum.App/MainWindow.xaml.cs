using System.Windows;
using System.Windows.Input;
using Lignum.App.ViewModels;
using Lignum.App.Views;
using Microsoft.Win32;

namespace Lignum.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel, AppearanceViewModel appearance)
    {
        _viewModel = viewModel;
        Appearance = appearance;
        DataContext = viewModel;
        InitializeComponent();
        Closed += (_, _) => _viewModel.Dispose();
        Loaded += (_, _) => LogView.Focus(); // arrow keys and PageUp/PageDown scroll the log straight away
    }

    /// <summary>Theme, font, zoom and tab width; XAML binds via RelativeSource AncestorType=Window.</summary>
    public AppearanceViewModel Appearance { get; }

    /// <summary>Ctrl+mouse wheel zooms the text anywhere in the window.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var command = e.Delta > 0 ? Appearance.ZoomInCommand : Appearance.ZoomOutCommand;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

    private void OnFontClick(object sender, RoutedEventArgs e) =>
        new FontDialog(Appearance) { Owner = this }.ShowDialog();

    private void OnOpenExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open log file",
            Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
            FilterIndex = 2,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _ = _viewModel.OpenFileAsync(dialog.FileName);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    // ---- detail pane (ticket 07). Clicking a row stands in for the caret/selection until tickets 05/08. ----

    private void OnLogViewLineClicked(object? sender, long line) => _viewModel.Detail.CurrentLine = line;

    private void OnFullLineRequested(object? sender, long line) => _viewModel.Detail.ShowFullLine(line);

    /// <summary>View > Show full line: the current line, or the top visible one if there is none yet.</summary>
    private void OnShowFullLineClick(object sender, RoutedEventArgs e)
    {
        var line = _viewModel.Detail.CurrentLine ?? LogView.LineAt(new Point(0, 0));
        if (line is { } l) _viewModel.Detail.ShowFullLine(l);
        else _viewModel.Detail.IsOpen = true;
    }
}
