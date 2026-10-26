using System.Collections.Immutable;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.App.Settings;
using Lignum.Core.Searching;

namespace Lignum.App.ViewModels;

/// <summary>One color's on/off toggle in the bookmarks pane's color filter.</summary>
public sealed partial class BookmarkColorToggle(BookmarkColor color) : ObservableObject
{
    public BookmarkColor Color { get; } = color;

    [ObservableProperty]
    private bool _isShown = true;
}

/// <summary>
/// What the bookmarks pane shows of one tab's bookmarks (ticket 22): sorted by line or color, filtered by color, by
/// "has a note" and by a wildcard search over the notes. View state only: never saved, never changes the bookmarks.
/// The color filter is per tab and is also what the margin shows (ticket 24).
/// </summary>
public sealed partial class BookmarkFilterViewModel : ObservableObject
{
    private readonly BookmarksViewModel _bookmarks;

    public BookmarkFilterViewModel(BookmarksViewModel bookmarks)
    {
        _bookmarks = bookmarks;
        ColorToggles = [.. Enum.GetValues<BookmarkColor>().Select(c => new BookmarkColorToggle(c))];
        foreach (var toggle in ColorToggles) toggle.PropertyChanged += (_, _) => OnFilterChanged();
        _bookmarks.PropertyChanged += OnBookmarksChanged;
    }

    /// <summary>One toggle per color, in <see cref="BookmarkColor"/> order; all shown at first.</summary>
    public IReadOnlyList<BookmarkColorToggle> ColorToggles { get; }

    /// <summary>Sort by color (in <see cref="BookmarkColor"/> order), then line; otherwise by line.</summary>
    [ObservableProperty]
    private bool _sortByColor;

    /// <summary>Only bookmarks with a note.</summary>
    [ObservableProperty]
    private bool _notesOnly;

    /// <summary>Case-insensitive search over the notes; <c>*</c> matches any run of characters. Empty = no search.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>The colors whose bookmarks are shown.</summary>
    public ImmutableHashSet<BookmarkColor> ShownColors =>
        [.. ColorToggles.Where(t => t.IsShown).Select(t => t.Color)];

    /// <summary>The bookmarks the pane lists, filtered and sorted.</summary>
    public IReadOnlyList<BookmarkItem> Items
    {
        get
        {
            var shown = ShownColors;
            var search = new WildcardPattern(SearchText);
            var items = _bookmarks.Items.Where(b => shown.Contains(b.Color)
                                                    && (!NotesOnly || b.Note is not null)
                                                    && (search.MatchesAll || (b.Note is { } note && search.IsMatch(note))));
            return [.. SortByColor ? items.OrderBy(b => b.Color).ThenBy(b => b.Line) : items];
        }
    }

    /// <summary>"Bookmarks (12)", or "Bookmarks (4 of 12)" while the filter hides some.</summary>
    public string Header
    {
        get
        {
            var total = _bookmarks.Lines.Count;
            var shown = Items.Count;
            return shown == total ? $"Bookmarks ({total:N0})" : $"Bookmarks ({shown:N0} of {total:N0})";
        }
    }

    private long? _selectedLine;

    /// <summary>
    /// The pane's selected bookmark (ticket 23), or null when there is none or the filter hides it. Setting null is
    /// ignored: the list sets it while its items are replaced, and the selection should survive that.
    /// </summary>
    public BookmarkItem? SelectedItem
    {
        get => _selectedLine is { } line ? Items.FirstOrDefault(b => b.Line == line) : null;
        set
        {
            if (value is null || value.Line == _selectedLine) return;
            _selectedLine = value.Line;
            OnSelectionChanged();
        }
    }

    /// <summary>
    /// The selected bookmark's note, edited in the pane. Setting it saves the note; empty removes the note and keeps
    /// the bookmark.
    /// </summary>
    public string SelectedNote
    {
        get => SelectedItem?.Note ?? "";
        set
        {
            if (SelectedItem is { } item && (item.Note ?? "") != value) _bookmarks.SetNote(item.Line, value);
        }
    }

    /// <summary>Whether a bookmark is selected, so its note can be edited.</summary>
    public bool HasSelection => SelectedItem is not null;

    partial void OnSortByColorChanged(bool value) => OnFilterChanged();

    partial void OnNotesOnlyChanged(bool value) => OnFilterChanged();

    partial void OnSearchTextChanged(string value) => OnFilterChanged();

    private void OnBookmarksChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BookmarksViewModel.Items)) return;
        if (_selectedLine is { } line && !_bookmarks.Lines.Contains(line)) _selectedLine = null; // removed
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Header));
        OnSelectionChanged();
    }

    private void OnFilterChanged()
    {
        OnPropertyChanged(nameof(ShownColors));
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Header));
        OnSelectionChanged();
    }

    // After Items: the list then selects the new row object for the same line.
    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectedNote));
        OnPropertyChanged(nameof(HasSelection));
    }
}
