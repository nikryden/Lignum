using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

/// <summary>One row of the bookmarks pane: a bookmarked 0-based line and its note, if any.</summary>
public sealed record BookmarkItem(long Line, string? Note)
{
    /// <summary>1-based, as the margin shows it.</summary>
    public long DisplayLine => Line + 1;
}

/// <summary>
/// The bookmarked lines of one file (ticket 17), with optional notes (ticket 06). Every change saves the file's <see cref="BookmarksFile"/> at once.
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
    }

    /// <summary>The bookmarks with their notes, ascending (the bookmarks pane).</summary>
    public IReadOnlyList<BookmarkItem> Items => [.. Lines.Select(l => new BookmarkItem(l, Notes.GetValueOrDefault(l)))];

    partial void OnLinesChanged(ImmutableSortedSet<long> value) => OnPropertyChanged(nameof(Items));

    partial void OnNotesChanged(ImmutableDictionary<long, string> value) => OnPropertyChanged(nameof(Items));

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
            Notes = Notes.Remove(line); // the note goes with its bookmark
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
        foreach (var entry in value.Bookmarks) entry.Note = Notes.GetValueOrDefault(entry.Line);
        _file.Save();
    }
}
