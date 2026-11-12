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
        InitializeTabWindows();
        _viewModel.Assistant.Approver = new DialogPermissionApprover();
        _viewModel.Assistant.PermissionsRequested += (_, _) => ShowSettings(SettingsPage.Ai);
        _viewModel.ConfirmClearBookmarks = notes => MessageBox.Show(this,
            $"{notes:N0} of the bookmarks to remove have notes. Remove them, with their notes and colors?",
            "Remove bookmarks", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
        _viewModel.ConfirmOpenFiles = count => MessageBox.Show(this,
            $"The folder has {count:N0} text files. Open them all, each in a tab?",
            "Open folder", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
        Closed += (_, _) =>
        {
            _viewModel.SaveSession(); // ticket 18: reopened on the next start
            CloseTabWindows(); // ticket 29: after saving, so the session still knows their tabs
            _viewModel.Dispose();
        };
    }

    /// <summary>Theme, font, zoom and tab width; XAML binds via RelativeSource AncestorType=Window.</summary>
    public AppearanceViewModel Appearance { get; }

    /// <summary>Line coloring rules and presets (ticket 14); viewers bind <c>Highlighting.Highlighter</c>.</summary>
    public HighlightingViewModel? Highlighting { get; init; }

    /// <summary>Syntax coloring by file type (ticket 25); viewers bind <c>Syntax.Highlighters</c>.</summary>
    public SyntaxViewModel? Syntax { get; init; }

    /// <summary>Opens Settings (ticket 30); the parameter, when a <see cref="SettingsPage"/>, picks the page.</summary>
    public static readonly RoutedUICommand SettingsCommand = new("Settings", nameof(SettingsCommand), typeof(MainWindow));

    private SettingsWindow? _settingsWindow;

    private void OnSettingsExecuted(object sender, ExecutedRoutedEventArgs e) =>
        ShowSettings(e.Parameter switch
        {
            SettingsPage page => page,
            string name when Enum.TryParse<SettingsPage>(name, out var named) => named,
            _ => null,
        });

    /// <summary>Brings the one Settings window to the front on <paramref name="page"/> (null: the page shown last).</summary>
    public void ShowSettings(SettingsPage? page = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
            [
                new(SettingsPage.Appearance, "Appearance", () => new AppearancePage(Appearance)),
                new(SettingsPage.HighlightRules, "Highlight rules", () => Highlighting is null ? null : new HighlightRulesPage(Highlighting)),
                new(SettingsPage.SyntaxSchemes, "Syntax schemes", () => Syntax is null ? null : new SyntaxSchemesPage(Syntax)),
                new(SettingsPage.ExternalEditor, "External editor",
                    () => new ExternalEditorPage(() => _viewModel.ExternalEditor, _viewModel.SetExternalEditor)),
                new(SettingsPage.Ai, "AI", () => new AiSettingsPage(_viewModel.Assistant)),
            ]) { Owner = this };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.ShowPage(page);
            _settingsWindow.Show();
            return;
        }

        _settingsWindow.ShowPage(page ?? SettingsWindow.LastPage);
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>Ctrl+mouse wheel zooms the text anywhere in the window.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var command = e.Delta > 0 ? Appearance.ZoomInCommand : Appearance.ZoomOutCommand;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

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
