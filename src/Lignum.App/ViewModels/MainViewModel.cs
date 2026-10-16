using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Lignum.Core;

namespace Lignum.App.ViewModels;

/// <summary>
/// The main window: the open tabs (one <see cref="LogTabViewModel"/> per file), which one is selected,
/// and app-wide state such as the recent-files list and open errors. Everything about one file lives in
/// its tab; this class only opens, switches and closes tabs.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const string AppName = "Lignum";

    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private readonly SettingsFile<AppSettings> _settings;

    /// <summary>The app-wide error line (a file that could not be opened).</summary>
    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private LogTabViewModel? _selectedTab;

    /// <param name="settings">The app's one settings.json (recent files are stored there).</param>
    public MainViewModel(SettingsFile<AppSettings> settings)
    {
        _settings = settings;
        LoadRecentFiles();
    }

    public ObservableCollection<LogTabViewModel> Tabs { get; } = [];

    public string Title => SelectedTab is { } tab ? $"{tab.FileName} - {AppName}" : AppName;

    /// <summary>
    /// Opens <paramref name="path"/> in a new tab and selects it, or selects its tab if it is already open
    /// (paths compared case-insensitively after resolving the full path). The returned task completes when
    /// that tab's indexing stops; the UI does not need to await it.
    /// </summary>
    public Task OpenFileAsync(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ErrorMessage = $"Could not open {path}: {ex.Message}";
            return Task.CompletedTask;
        }

        var existing = FindTab(fullPath);
        if (existing is not null)
        {
            ErrorMessage = null;
            SelectedTab = existing;
            AddRecentFile(existing.Path);
            return existing.IndexingCompletion;
        }

        LogDocument document;
        try
        {
            document = LogDocument.Open(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            ErrorMessage = $"Could not open {path}: {ex.Message}";
            return Task.CompletedTask;
        }

        ErrorMessage = null;
        var tab = new LogTabViewModel(document, _uiContext);
        Tabs.Add(tab);
        SelectedTab = tab;
        AddRecentFile(tab.Path);
        return tab.Start();
    }

    /// <summary>
    /// Opens each path (e.g. files dropped on the window) as <see cref="OpenFileAsync"/> does; the last one
    /// ends up selected. Completes when all of them have finished indexing.
    /// </summary>
    public Task OpenFilesAsync(IEnumerable<string> paths) =>
        Task.WhenAll(paths.Select(OpenFileAsync).ToList());

    /// <summary>
    /// Closes <paramref name="tab"/> (null = the selected tab, as Ctrl+W does) and releases its file.
    /// Closing the selected tab selects the tab that took its place, or the one before it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab(LogTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null) return;
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        if (ReferenceEquals(tab, SelectedTab))
        {
            SelectedTab = Tabs.Count > 1 ? Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1] : null;
        }

        Tabs.RemoveAt(index);
        tab.Dispose();
        CloseTabCommand.NotifyCanExecuteChanged();
    }

    private bool CanCloseTab(LogTabViewModel? tab) => (tab ?? SelectedTab) is not null;

    public void Dispose()
    {
        foreach (var tab in Tabs) tab.Dispose();
    }

    partial void OnSelectedTabChanged(LogTabViewModel? oldValue, LogTabViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    private LogTabViewModel? FindTab(string fullPath) =>
        Tabs.FirstOrDefault(t => string.Equals(t.Path, fullPath, StringComparison.OrdinalIgnoreCase));
}
