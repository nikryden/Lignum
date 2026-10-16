using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Lignum.App.Settings;

namespace Lignum.App.ViewModels;

/// <summary>
/// The bookmarked lines of one file (ticket 17). Every change saves the file's <see cref="BookmarksFile"/> at once.
/// <see cref="Lines"/> is replaced (not mutated) on every change, so a binding to it repaints the margin markers.
/// </summary>
public sealed partial class BookmarksViewModel : ObservableObject
{
    private readonly SettingsFile<BookmarksFile>? _file;

    /// <summary>Bookmarked 0-based file lines, ascending. Lines past the end of the file are kept.</summary>
    [ObservableProperty]
    private ImmutableSortedSet<long> _lines;

    /// <param name="file">Where the bookmarks are loaded from and saved to; null keeps them in memory only.</param>
    /// <param name="path">The log file's full path, written into the file for people reading it.</param>
    public BookmarksViewModel(SettingsFile<BookmarksFile>? file, string path)
    {
        _file = file;
        if (file is not null) file.Value.Path = path;
        _lines = file?.Value.Bookmarks.Where(b => b.Line >= 0).Select(b => b.Line).ToImmutableSortedSet()
                 ?? ImmutableSortedSet<long>.Empty;
    }

    /// <summary>Adds a bookmark on <paramref name="line"/>, or removes the one that is there.</summary>
    public void Toggle(long line)
    {
        if (line < 0) return;
        Lines = Lines.Contains(line) ? Lines.Remove(line) : Lines.Add(line);
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
        _file.Save();
    }
}
