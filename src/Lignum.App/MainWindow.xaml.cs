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
        InitializeFindWindow();
        _viewModel.Assistant.Approver = new DialogPermissionApprover();
        _viewModel.Assistant.PermissionsRequested += (_, _) => OnAiPermissionsClick(this, new RoutedEventArgs());
        _viewModel.ConfirmClearBookmarks = notes => MessageBox.Show(this,
            $"{notes:N0} of the bookmarks to remove have notes. Remove them, with their notes and colors?",
            "Remove bookmarks", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
        _viewModel.ConfirmOpenFiles = count => MessageBox.Show(this,
            $"The folder has {count:N0} text files. Open them all, each in a tab?",
            "Open folder", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
        Closed += (_, _) =>
        {
            _viewModel.SaveSession(); // ticket 18: reopened on the next start
            _viewModel.Dispose();
        };
    }

    /// <summary>Theme, font, zoom and tab width; XAML binds via RelativeSource AncestorType=Window.</summary>
    public AppearanceViewModel Appearance { get; }

    /// <summary>Line coloring rules and presets (ticket 14); viewers bind <c>Highlighting.Highlighter</c>.</summary>
    public HighlightingViewModel? Highlighting { get; init; }

    private void OnHighlightRulesClick(object sender, RoutedEventArgs e)
    {
        if (Highlighting is not null) new HighlightRulesDialog(Highlighting) { Owner = this }.ShowDialog();
    }

    private void OnAiPermissionsClick(object sender, RoutedEventArgs e) =>
        new AiPermissionsDialog(_viewModel.Assistant.Permissions) { Owner = this }.ShowDialog();

    private void OnExternalEditorClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ExternalEditorDialog(_viewModel.ExternalEditor) { Owner = this };
        if (dialog.ShowDialog() == true) _viewModel.SetExternalEditor(dialog.Command, dialog.Arguments);
    }

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
            Title = "Open log files",
            Filter = "Log files (*.log;*.txt)|*.log;*.txt|Lignum packages with notes (*.lm)|*.lm|All files (*.*)|*.*",
            FilterIndex = 3,
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _ = _viewModel.OpenFilesAsync(dialog.FileNames);
        }
    }

    /// <summary>Opens the text files directly in a folder, each in a tab (binary files are skipped).</summary>
    private void OnOpenFolderExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Open all text files in a folder" };
        if (dialog.ShowDialog(this) == true)
        {
            _ = _viewModel.OpenFolderAsync(dialog.FolderName);
        }
    }

    /// <summary>Accept files dragged from Explorer (Preview, so the viewer and text boxes don't refuse them).</summary>
    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Dropping files opens each in a tab (or switches to it if already open); a folder opens its text files.</summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        e.Handled = true;
        _ = _viewModel.OpenDroppedAsync(paths);
        Activate();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnExportClick(object sender, RoutedEventArgs e) => Export(compress: true);

    private void OnExportUncompressedClick(object sender, RoutedEventArgs e) => Export(compress: false);

    /// <summary>File > Export with notes (ticket 06): the selected tab's log, bookmarks and notes as a .lm package.</summary>
    private void Export(bool compress)
    {
        if (_viewModel.SelectedTab is not { } tab) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export with notes",
            Filter = "Lignum package with notes (*.lm)|*.lm",
            FileName = Path.ChangeExtension(tab.FileName, ".lm"),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) == true) _ = _viewModel.ExportPackageAsync(dialog.FileName, compress);
    }
}
