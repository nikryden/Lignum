using Lignum.App.Packaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lignum.App.Settings;
using Lignum.App.Viewer;

namespace Lignum.App.ViewModels;

// Bookmarks (ticket 17): Ctrl+F2 toggles the caret line, F2 / Shift+F2 jump to the next / previous one.
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
                if (e.PropertyName == nameof(BookmarksViewModel.Lines)) ClearBookmarksCommand.NotifyCanExecuteChanged();
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

    /// <summary>
    /// Ctrl+F2 / "Toggle bookmark": the caret line of <paramref name="selection"/> (the results pane passes its
    /// own); null = the main view's.
    /// </summary>
    [RelayCommand]
    private void ToggleBookmark(TextSelection? selection) =>
        Bookmarks.Toggle((selection ?? Selection).Caret.Line);

    /// <summary>Double-click in the line-number margin (ticket 04): toggles that 0-based file line.</summary>
    [RelayCommand]
    private void ToggleBookmarkAt(long line) => Bookmarks.Toggle(line);

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

    /// <summary>F2: the next bookmark after the caret, wrapping to the first.</summary>
    [RelayCommand]
    private void NextBookmark() => JumpToBookmark(Bookmarks.Next(Navigation.Caret.Line, LineCount));

    /// <summary>Shift+F2: the previous bookmark before the caret, wrapping to the last.</summary>
    [RelayCommand]
    private void PreviousBookmark() => JumpToBookmark(Bookmarks.Previous(Navigation.Caret.Line, LineCount));

    private void JumpToBookmark(long? line)
    {
        if (line is { } target) Navigation.GoToLine(target);
    }
}
