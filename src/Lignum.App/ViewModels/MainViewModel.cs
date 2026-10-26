using Lignum.App.Packaging;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Lignum.App.Views;
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
        History = new SearchHistory(settings);
        _highlightMatchesOutsideFilter = settings.Value.HighlightMatchesOutsideFilter;
        LoadRecentFiles();
    }

    /// <summary>The find and filter history shared by every tab (the box dropdowns).</summary>
    public SearchHistory History { get; }

    public ObservableCollection<LogTabViewModel> Tabs { get; } = [];

    /// <summary>Copies selections in every tab (the clipboard and the large-copy prompt); tests replace it.</summary>
    public SelectionCopier Copier { get; init; } = new(new WpfClipboard(), new DialogCopyPrompt());

    /// <summary>"Clear all bookmarks" asks this, given how many bookmarks have notes; null: clear without asking.</summary>
    public Func<int, bool>? ConfirmClearBookmarks { get; set; }

    /// <summary>The app-wide highlight rules; "Add as highlight rule" (ticket 15) adds to them. Null: no such item.</summary>
    public HighlightingViewModel? Highlighting { get; init; }

    public string Title => SelectedTab is { } tab ? $"{tab.FileName} - {AppName}" : AppName;

    /// <summary>
    /// Opens <paramref name="path"/> in a new tab and selects it, or selects its tab if it is already open
    /// (paths compared case-insensitively after resolving the full path). The returned task completes when
    /// that tab's indexing stops; the UI does not need to await it.
    /// </summary>
    public async Task OpenFileAsync(string path) => await await OpenFileCoreAsync(path);

    /// <summary>Opens <paramref name="path"/> as <see cref="OpenFileAsync"/> does; completes once its tab is there, with the tab's indexing.</summary>
    internal async Task<Task> OpenFileCoreAsync(string path)
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

        if (FindTab(fullPath) is { } existing) return SelectExisting(existing);

        if (await AddTabAsync(fullPath, path) is not { } added) return Task.CompletedTask;
        if (!added.IsNew) return SelectExisting(added.Tab);
        SelectedTab = added.Tab;
        AddRecentFile(added.Tab.SourcePath);
        return added.Tab.Start();
    }

    private Task SelectExisting(LogTabViewModel tab)
    {
        ErrorMessage = null;
        SelectedTab = tab;
        AddRecentFile(tab.SourcePath);
        return tab.IndexingCompletion;
    }

    /// <summary>
    /// Opens <paramref name="fullPath"/> (a log, or a <c>.lm</c> package) and appends its tab, not yet indexing.
    /// IsNew is false when a tab for the path appeared meanwhile (e.g. dropped twice); that tab is returned.
    /// Null (and <see cref="ErrorMessage"/> set) when it cannot be opened.
    /// </summary>
    private async Task<(LogTabViewModel Tab, bool IsNew)?> AddTabAsync(string fullPath, string displayPath)
    {
        OpenedPackage? package = null;
        if (LogPackage.IsPackage(fullPath) && (package = await OpenPackageAsync(fullPath, displayPath)) is null)
            return null;

        var document = await OpenDocumentAsync(package?.LogPath ?? fullPath, displayPath, package?.Manifest.Encoding);
        if (document is null || FindTab(fullPath) is { } opened)
        {
            document?.Dispose();
            if (package is not null) LogPackage.DeleteExtracted(package.Folder);
            return document is null ? null : (FindTab(fullPath)!, false);
        }

        var tab = AddTab(document, package);
        if (package is { ChecksumMatches: false })
        {
            ErrorMessage = $"Warning: the log in {displayPath} does not match the checksum in the package. "
                           + "It may be damaged or changed.";
        }

        return (tab, true);
    }

    /// <summary>
    /// Opens <paramref name="fullPath"/> on a background thread, so a slow disk or share never blocks the UI.
    /// Null (and <see cref="ErrorMessage"/> set) when the file cannot be opened.
    /// </summary>
    private async Task<LogDocument?> OpenDocumentAsync(string fullPath, string displayPath, LogEncoding? encoding = null)
    {
        try
        {
            var document = await LogDocument.OpenAsync(fullPath, encoding);
            ErrorMessage = null;
            return document;
        }
        catch (Exception ex) when (LogDocument.IsOpenFailure(ex))
        {
            ErrorMessage = $"Could not open {displayPath}: {ex.Message}";
            return null;
        }
    }

    /// <summary>Appends a tab for <paramref name="document"/>, not yet indexing (call <see cref="LogTabViewModel.Start"/>).</summary>
    private LogTabViewModel AddTab(LogDocument document, OpenedPackage? package = null)
    {
        var tab = new LogTabViewModel(document, _uiContext,
            new TabServices(Copier, Highlighting, History, _settings.Store,
                notes => ConfirmClearBookmarks?.Invoke(notes) ?? true, OpenNoteLink))
        {
            Package = package,
            EncodingOverride = package?.Manifest.Encoding,
        };
        tab.Search.HighlightOutsideFilter = HighlightMatchesOutsideFilter;
        tab.Detail.IncludeHiddenLines = DetailPane.IncludeHiddenLines;
        Tabs.Add(tab);
        return tab;
    }

    /// <summary>
    /// Opens each path (e.g. files dropped on the window) as <see cref="OpenFileAsync"/> does; the last one
    /// ends up selected. Completes when all of them have finished indexing.
    /// </summary>
    public async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        var indexing = new List<Task>();
        foreach (var path in paths) indexing.Add(await OpenFileCoreAsync(path)); // one by one, so the tabs keep their order
        await Task.WhenAll(indexing);
    }

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
        Tabs.FirstOrDefault(t => string.Equals(t.SourcePath, fullPath, StringComparison.OrdinalIgnoreCase));
}
