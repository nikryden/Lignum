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

    /// <summary>Accept files dragged from Explorer (Preview, so the viewer and text boxes don't refuse them).</summary>
    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Dropping one or more files opens each in a tab (or switches to it if already open).</summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        e.Handled = true;
        _ = _viewModel.OpenFilesAsync(paths);
        Activate();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();
}
