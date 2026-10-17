using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

/// <summary>One row of the bookmarks pane: a bookmarked 0-based line, its note, if any, and its color.</summary>
public sealed record BookmarkItem(long Line, string? Note, BookmarkColor Color = BookmarkColor.Blue)
{
    /// <summary>1-based, as the margin shows it.</summary>
    public long DisplayLine => Line + 1;
}

/// <summary>
/// The bookmarked lines of one file (ticket 17), with optional notes (ticket 06) and colors. Every change saves the file's <see cref="BookmarksFile"/> at once.
/// <see cref="Lines"/> is replaced (not mutated) on every change, so a binding to it repaints the margin markers.
/// </summary>
public sealed partial class BookmarksViewModel : ObservableObject
{
    private readonly SettingsFile<BookmarksFile>? _file;

    /// <summary>Bookmarked 0-based file lines, ascending. Lines past the end of the file are kept.</summary>
    [ObservableProperty]
    private ImmutableSortedSet<long> _lines;

    /// <summary>Notes by 0-based line; every key is also in <see cref="Lines"/>. Replaced on every change.</summary>
    [ObservableProperty]
    private ImmutableDictionary<long, string> _notes;

    /// <summary>
    /// Colors by 0-based line; every key is also in <see cref="Lines"/>, and a line that is not a key is
    /// <see cref="BookmarkColor.Blue"/>. Replaced on every change.
    /// </summary>
    [ObservableProperty]
    private ImmutableDictionary<long, BookmarkColor> _colors;

    /// <param name="file">Where the bookmarks are loaded from and saved to; null keeps them in memory only.</param>
    /// <param name="path">The log file's full path, written into the file for people reading it.</param>
    public BookmarksViewModel(SettingsFile<BookmarksFile>? file, string path)
    {
        _file = file;
        if (file is not null) file.Value.Path = path;
        _lines = file?.Value.Bookmarks.Where(b => b.Line >= 0).Select(b => b.Line).ToImmutableSortedSet()
                 ?? ImmutableSortedSet<long>.Empty;
        _notes = file?.Value.Bookmarks.Where(b => b.Line >= 0 && !string.IsNullOrWhiteSpace(b.Note))
                     .GroupBy(b => b.Line).ToImmutableDictionary(g => g.Key, g => g.First().Note!)
                 ?? ImmutableDictionary<long, string>.Empty;
        _colors = file?.Value.Bookmarks.Where(b => b.Line >= 0 && b.Color is { } c && c != BookmarkColor.Blue)
                      .GroupBy(b => b.Line).ToImmutableDictionary(g => g.Key, g => g.First().Color!.Value)
                  ?? ImmutableDictionary<long, BookmarkColor>.Empty;
    }

    /// <summary>The bookmarks with their notes, ascending (the bookmarks pane).</summary>
    public IReadOnlyList<BookmarkItem> Items =>
        [.. Lines.Select(l => new BookmarkItem(l, Notes.GetValueOrDefault(l), ColorFor(l)))];

    partial void OnLinesChanged(ImmutableSortedSet<long> value) => OnPropertyChanged(nameof(Items));

    partial void OnNotesChanged(ImmutableDictionary<long, string> value) => OnPropertyChanged(nameof(Items));

    partial void OnColorsChanged(ImmutableDictionary<long, BookmarkColor> value) => OnPropertyChanged(nameof(Items));

    /// <summary>The color of the bookmark on <paramref name="line"/> (<see cref="BookmarkColor.Blue"/> by default).</summary>
    public BookmarkColor ColorFor(long line) => Colors.GetValueOrDefault(line, BookmarkColor.Blue);

    /// <summary>Sets the color of the bookmark on <paramref name="line"/>, bookmarking the line if it is not yet.</summary>
    public void SetColor(long line, BookmarkColor color)
    {
        if (line < 0) return;
        if (Lines.Contains(line) && ColorFor(line) == color) return;
        if (!Lines.Contains(line)) Lines = Lines.Add(line);
        Colors = color == BookmarkColor.Blue ? Colors.Remove(line) : Colors.SetItem(line, color);
        Save();
    }

    /// <summary>The note on <paramref name="line"/>, or null.</summary>
    public string? NoteFor(long line) => Notes.GetValueOrDefault(line);

    /// <summary>
    /// Sets the note on <paramref name="line"/>, bookmarking the line if it is not yet. An empty note removes
    /// the note and keeps the bookmark.
    /// </summary>
    public void SetNote(long line, string? note)
    {
        if (line < 0) return;
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is null)
        {
            if (!Notes.ContainsKey(line)) return;
            Notes = Notes.Remove(line);
        }
        else
        {
            if (!Lines.Contains(line)) Lines = Lines.Add(line);
            Notes = Notes.SetItem(line, note);
        }

        Save();
    }

    /// <summary>Adds a bookmark on <paramref name="line"/>, or removes the one that is there.</summary>
    public void Toggle(long line)
    {
        if (line < 0) return;
        if (Lines.Contains(line))
        {
            Lines = Lines.Remove(line);
            Notes = Notes.Remove(line); // the note and color go with their bookmark
            Colors = Colors.Remove(line);
        }
        else
        {
            Lines = Lines.Add(line);
        }

        Save();
    }

    /// <summary>The first bookmark after <paramref name="line"/>, wrapping to the first; only lines below <paramref name="lineCount"/>.</summary>
    public long? Next(long line, long lineCount)
    {
        var usable = Lines.Where(l => l < lineCount).ToList();
        if (usable.Count == 0) return null;
        foreach (var candidate in usable)
        {
            if (candidate > line) return candidate;
        }

        return usable[0];
    }

    /// <summary>The last bookmark before <paramref name="line"/>, wrapping to the last; only lines below <paramref name="lineCount"/>.</summary>
    public long? Previous(long line, long lineCount)
    {
        var usable = Lines.Where(l => l < lineCount).ToList();
        if (usable.Count == 0) return null;
        for (var i = usable.Count - 1; i >= 0; i--)
        {
            if (usable[i] < line) return usable[i];
        }

        return usable[^1];
    }

    private void Save()
    {
        if (_file is null) return;
        var value = _file.Value;
        // Keep each surviving entry object (and any fields a newer build wrote on it, such as a note).
        var existing = value.Bookmarks.GroupBy(b => b.Line).ToDictionary(g => g.Key, g => g.First());
        value.Bookmarks = Lines.Select(l => existing.TryGetValue(l, out var entry) ? entry : new BookmarkEntry { Line = l })
            .ToList();
        foreach (var entry in value.Bookmarks)
        {
            entry.Note = Notes.GetValueOrDefault(entry.Line);
            entry.Color = Colors.TryGetValue(entry.Line, out var color) ? color : null;
        }
        _file.Save();
    }
}
