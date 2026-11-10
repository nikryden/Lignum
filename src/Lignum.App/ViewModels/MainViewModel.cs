using Lignum.App.Packaging;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Lignum.App.Views;
using Lignum.Core;
using Lignum.Core.Searching;

namespace Lignum.App.ViewModels;

/// <summary>
/// The main window: the open tabs (one <see cref="LogTabViewModel"/> per file), which one is selected,
/// and app-wide state such as the recent-files list and open errors. Everything about one file lives in
/// its tab; this class only opens, switches and closes tabs.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private readonly SettingsFile<AppSettings> _settings;

    /// <summary>The app-wide error line (a file that could not be opened).</summary>
    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToNewWindowCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToMainWindowCommand))]
    private LogTabViewModel? _selectedTab;

    /// <param name="settings">The app's one settings.json (recent files are stored there).</param>
    public MainViewModel(SettingsFile<AppSettings> settings)
    {
        _settings = settings;
        History = new SearchHistory(settings);
        _highlightMatchesOutsideFilter = settings.Value.HighlightMatchesOutsideFilter;
        LoadRecentFiles();
        MainGroup = new TabGroupViewModel(isMain: true);
        ActiveGroup = MainGroup;
        Watch(MainGroup);
    }

    /// <summary>The find and filter history shared by every tab (the box dropdowns).</summary>
    public SearchHistory History { get; }

    /// <summary>Every open tab, in every window (all-tabs search, the AI tools, close all). Each window's order is in its group.</summary>
    public ObservableCollection<LogTabViewModel> Tabs { get; } = [];

    /// <summary>Copies selections in every tab (the clipboard and the large-copy prompt); tests replace it.</summary>
    public SelectionCopier Copier { get; init; } = new(new WpfClipboard(), new DialogCopyPrompt());

    /// <summary>"Clear all bookmarks" asks this, given how many bookmarks have notes; null: clear without asking.</summary>
    public Func<int, bool>? ConfirmClearBookmarks { get; set; }

    /// <summary>The app-wide highlight rules; "Add as highlight rule" (ticket 15) adds to them. Null: no such item.</summary>
    public HighlightingViewModel? Highlighting { get; init; }

    /// <summary>The main window's title: the file it shows (a floating window has its own, <see cref="TabGroupViewModel.Title"/>).</summary>
    public string Title => TabGroupViewModel.TitleFor(MainGroup.SelectedTab);

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
    private async Task<(LogTabViewModel Tab, bool IsNew)?> AddTabAsync(string fullPath, string displayPath,
        TabGroupViewModel? group = null)
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

        var tab = AddTab(document, package, group);
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

    /// <summary>
    /// Appends a tab for <paramref name="document"/> to <paramref name="group"/> (null: the active window's),
    /// not yet indexing (call <see cref="LogTabViewModel.Start"/>).
    /// </summary>
    private LogTabViewModel AddTab(LogDocument document, OpenedPackage? package = null, TabGroupViewModel? group = null)
    {
        var tab = new LogTabViewModel(document, _uiContext,
            new TabServices(Copier, Highlighting, History, _settings.Store,
                notes => ConfirmClearBookmarks?.Invoke(notes) ?? true, OpenNoteLinkAsync))
        {
            Package = package,
            EncodingOverride = package?.Manifest.Encoding,
        };
        tab.Search.HighlightOutsideFilter = HighlightMatchesOutsideFilter;
        tab.Detail.IncludeHiddenLines = DetailPane.IncludeHiddenLines;
        Tabs.Add(tab);
        (group ?? ActiveGroup).Add(tab);
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

    /// <summary>Opening a folder with more text files than this asks <see cref="ConfirmOpenFiles"/> first.</summary>
    public const int ConfirmOpenFilesAbove = 20;

    /// <summary>Opening a folder of many files asks this, given how many would open; null: open without asking.</summary>
    public Func<int, bool>? ConfirmOpenFiles { get; set; }

    /// <summary>
    /// Opens the text files directly in <paramref name="folder"/> (not its subfolders) as <see cref="OpenFilesAsync"/>
    /// does; binary and unreadable files are skipped and <see cref="ErrorMessage"/> says how many. The folder is
    /// listed on a background thread. Completes when all of them have finished indexing.
    /// </summary>
    public async Task OpenFolderAsync(string folder)
    {
        var indexing = new List<Task>();
        await OpenFolderCoreAsync(folder, indexing);
        await Task.WhenAll(indexing);
    }

    /// <summary>Opens <paramref name="folder"/> as <see cref="OpenFolderAsync"/> does; completes once its tabs are there, adding their indexing to <paramref name="indexing"/>.</summary>
    private async Task OpenFolderCoreAsync(string folder, List<Task> indexing)
    {
        List<string> files;
        int binary, unreadable;
        try
        {
            (files, binary, unreadable) = await Task.Run(() => FileFinder.FindTextFiles(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorMessage = $"Could not open the folder {folder}: {ex.Message}";
            return;
        }

        var skipped = string.Join(", ", new[] { (binary, "binary"), (unreadable, "unreadable") }
            .Where(s => s.Item1 > 0).Select(s => $"{s.Item1:N0} {s.Item2}"));
        if (files.Count == 0)
        {
            ErrorMessage = $"No text files in {folder}" + (skipped.Length > 0 ? $" (skipped {skipped})." : ".");
            return;
        }
        if (files.Count > ConfirmOpenFilesAbove && ConfirmOpenFiles is { } confirm && !confirm(files.Count)) return;

        foreach (var file in files) indexing.Add(await OpenFileCoreAsync(file)); // one by one, so the tabs keep their order
        if (skipped.Length > 0 && ErrorMessage is null)
            ErrorMessage = $"Opened {files.Count:N0} files from {folder}; skipped {skipped}.";
    }

    /// <summary>
    /// Opens what was dropped on the window, in order: files as <see cref="OpenFileAsync"/> does, folders as
    /// <see cref="OpenFolderAsync"/> does. Completes when all of them have finished indexing.
    /// </summary>
    public async Task OpenDroppedAsync(IReadOnlyList<string> paths)
    {
        var folders = await Task.Run(() => paths.Where(Directory.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var indexing = new List<Task>();
        foreach (var path in paths)
        {
            if (folders.Contains(path)) await OpenFolderCoreAsync(path, indexing);
            else indexing.Add(await OpenFileCoreAsync(path));
        }
        await Task.WhenAll(indexing);
    }

    /// <summary>
    /// Closes <paramref name="tab"/> (null = the active tab, as Ctrl+W does) and releases its file.
    /// Closing the tab shown in its window shows the tab that took its place, or the one before it;
    /// a floating window left with no tabs closes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab(LogTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null || !Tabs.Contains(tab)) return;

        TakeOut(tab);
        Tabs.Remove(tab);
        tab.Dispose();
        CloseTabCommand.NotifyCanExecuteChanged();
        NotifyMoveCommands();
    }

    private bool CanCloseTab(LogTabViewModel? tab) => (tab ?? SelectedTab) is not null;

    public void Dispose()
    {
        foreach (var tab in Tabs) tab.Dispose();
    }

    /// <summary>
    /// Setting the active tab shows it in its window and makes that window the active one (the window
    /// is brought to the front by the view, ticket 29).
    /// </summary>
    partial void OnSelectedTabChanged(LogTabViewModel? value)
    {
        if (value is null || GroupOf(value) is not { } group) return;
        ActiveGroup = group;
        group.SelectedTab = value;
    }

    private LogTabViewModel? FindTab(string fullPath) =>
        Tabs.FirstOrDefault(t => string.Equals(t.SourcePath, fullPath, StringComparison.OrdinalIgnoreCase));
}
