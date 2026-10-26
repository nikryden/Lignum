using Lignum.App.Packaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

// Bookmarks (ticket 17): Ctrl+F2 toggles the caret line, F2 / Shift+F2 jump to the next / previous shown one.
// They belong to the file (bookmarks/<hash of the path>.json), so they survive a reload and a restart.
public sealed partial class LogTabViewModel
{
    private BookmarksViewModel? _bookmarks;

    private SettingsStore? SettingsStore => _services.SettingsStore;

    /// <summary>This file's bookmarks, loaded on first use.</summary>
    public BookmarksViewModel Bookmarks
    {
        get
        {
            if (_bookmarks is not null) return _bookmarks;
            _bookmarks = new BookmarksViewModel(
                Package is { } package
                    // A package keeps its own bookmarks next to the extracted log, apart from the user's own.
                    ? new SettingsStore(package.Folder, Serilog.Log.Logger).Open<BookmarksFile>(LogPackage.BookmarksEntry)
                    : SettingsStore?.Open<BookmarksFile>(BookmarksFile.RelativePathFor(Path)), Path);
            _bookmarks.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(BookmarksViewModel.Lines)) return;
                ClearBookmarksCommand.NotifyCanExecuteChanged();
                RemoveBookmarksWithoutNotesCommand.NotifyCanExecuteChanged();
                RemoveBookmarksOfColorCommand.NotifyCanExecuteChanged();
            };
            return _bookmarks;
        }
    }

    /// <summary>
    /// "Clear all bookmarks" (ticket 11): removes every bookmark of this file, with its note and color. Asks first
    /// when any bookmark has a note.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasBookmarks))]
    private void ClearBookmarks()
    {
        var notes = Bookmarks.Notes.Count;
        if (notes > 0 && _services.ConfirmClearBookmarks is { } confirm && !confirm(notes)) return;
        Bookmarks.Clear();
    }

    private bool HasBookmarks() => !Bookmarks.Lines.IsEmpty;

    /// <summary>"Remove bookmarks without notes" (ticket 24). Nothing with a note goes, so it never asks.</summary>
    [RelayCommand(CanExecute = nameof(HasBookmarks))]
    private void RemoveBookmarksWithoutNotes() =>
        Bookmarks.Remove(Bookmarks.Lines.Where(l => !Bookmarks.Notes.ContainsKey(l)));

    /// <summary>"Remove bookmarks of color" (ticket 24): asks first when any of them has a note.</summary>
    [RelayCommand(CanExecute = nameof(HasBookmarks))]
    private void RemoveBookmarksOfColor(BookmarkColor color)
    {
        var lines = Bookmarks.Lines.Where(l => Bookmarks.ColorFor(l) == color).ToList();
        var notes = lines.Count(Bookmarks.Notes.ContainsKey);
        if (notes > 0 && _services.ConfirmClearBookmarks is { } confirm && !confirm(notes)) return;
        Bookmarks.Remove(lines);
    }

    /// <summary>
    /// Ctrl+F2 / "Toggle bookmark": the caret line of <paramref name="selection"/> (the results pane passes its
    /// own); null = the main view's.
    /// </summary>
    [RelayCommand]
    private void ToggleBookmark(TextSelection? selection) => ToggleShown((selection ?? Selection).Caret.Line);

    /// <summary>Double-click in the line-number margin (ticket 04): toggles that 0-based file line.</summary>
    [RelayCommand]
    private void ToggleBookmarkAt(long line) => ToggleShown(line);

    /// <summary>
    /// Toggles the bookmark on <paramref name="line"/>, unless its color is hidden (ticket 24): then nothing happens, so
    /// a bookmark the user cannot see is never removed by accident. "Clear all" still removes hidden ones.
    /// </summary>
    private void ToggleShown(long line)
    {
        if (Bookmarks.Lines.Contains(line) && !BookmarkFilter.VisibleLines.Contains(line)) return;
        Bookmarks.Toggle(line);
    }

    private BookmarkFilterViewModel? _bookmarkFilter;

    /// <summary>What the bookmarks pane lists (ticket 22): sort, color, notes-only and note search. Not saved.</summary>
    public BookmarkFilterViewModel BookmarkFilter => _bookmarkFilter ??= new BookmarkFilterViewModel(Bookmarks);

    /// <summary>Whether the bookmarks pane (every bookmark and note in this file) is shown.</summary>
    [ObservableProperty]
    private bool _isBookmarksPaneOpen;

    [RelayCommand]
    private void ToggleBookmarksPane() => IsBookmarksPaneOpen = !IsBookmarksPaneOpen;

    /// <summary>A click in the bookmarks pane: moves the caret to that bookmark's line.</summary>
    [RelayCommand]
    private void GoToBookmark(BookmarkItem? item)
    {
        if (item is not null) Navigation.GoToLine(item.Line);
    }

    /// <summary>Ctrl+click on a link in a bookmark note (ticket 23): a browser, a tab or Explorer.</summary>
    [RelayCommand]
    private Task OpenNoteLink(NoteLink? link) =>
        link is not null && _services.OpenNoteLink is { } open ? open(link, false) : Task.CompletedTask;

    /// <summary>"Show in folder" on a path in a bookmark note: Explorer with the file or folder selected.</summary>
    [RelayCommand]
    private Task ShowNoteLinkInFolder(NoteLink? link) =>
        link is { Kind: NoteLinkKind.Path } && _services.OpenNoteLink is { } open ? open(link, true) : Task.CompletedTask;

    /// <summary>"Copy description" in the bookmarks pane.</summary>
    [RelayCommand]
    private void CopyBookmarkNote(BookmarkItem? item)
    {
        if (item?.Note is { } note) _services.Copier?.SetText(note);
    }

    /// <summary>F2: the next bookmark after the caret, wrapping to the first.</summary>
    [RelayCommand]
    private void NextBookmark() =>
        JumpToBookmark(Bookmarks.Next(Navigation.Caret.Line, LineCount, BookmarkFilter.VisibleLines));

    /// <summary>Shift+F2: the previous bookmark before the caret, wrapping to the last.</summary>
    [RelayCommand]
    private void PreviousBookmark() =>
        JumpToBookmark(Bookmarks.Previous(Navigation.Caret.Line, LineCount, BookmarkFilter.VisibleLines));

    private void JumpToBookmark(long? line)
    {
        if (line is { } target) Navigation.GoToLine(target);
    }
}
