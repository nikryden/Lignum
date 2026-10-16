using System.Windows;
using System.Windows.Input;
using Lignum.App.ViewModels;
using Microsoft.Win32;

namespace Lignum.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Closed += (_, _) => _viewModel.Dispose();
        Loaded += (_, _) => LogView.Focus(); // arrow keys and PageUp/PageDown scroll the log straight away
    }

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
}
