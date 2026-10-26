using Lignum.App.Settings;
using Lignum.App.ViewModels;

namespace Lignum.App.Tests;

/// <summary>The bookmarks pane's count, sort and filters (ticket 22).</summary>
public sealed class BookmarkFilterTests
{
    /// <summary>Lines 1 (blue, "disk full"), 3 (red), 5 (red, "timeout"), 7 (green, "Disk quota").</summary>
    private static (BookmarksViewModel Bookmarks, BookmarkFilterViewModel Filter) Create()
    {
        var bookmarks = new BookmarksViewModel(null, @"C:\logs\a.log");
        bookmarks.SetNote(1, "disk full");
        bookmarks.SetColor(3, BookmarkColor.Red);
        bookmarks.SetColor(5, BookmarkColor.Red);
        bookmarks.SetNote(5, "timeout");
        bookmarks.SetColor(7, BookmarkColor.Green);
        bookmarks.SetNote(7, "Disk quota");
        return (bookmarks, new BookmarkFilterViewModel(bookmarks));
    }

    private static long[] Lines(BookmarkFilterViewModel filter) => [.. filter.Items.Select(b => b.Line)];

    [Fact]
    public void Shows_every_bookmark_by_line_with_the_count()
    {
        var (_, filter) = Create();

        Assert.Equal([1L, 3, 5, 7], Lines(filter));
        Assert.Equal("Bookmarks (4)", filter.Header);
    }

    [Fact]
    public void Sorts_by_color_then_line()
    {
        var (_, filter) = Create();

        filter.SortByColor = true;

        Assert.Equal([1L, 3, 5, 7], Lines(filter)); // blue, red, red, green
        filter.ColorToggles.Single(t => t.Color == BookmarkColor.Blue).IsShown = false;
        Assert.Equal([3L, 5, 7], Lines(filter));
    }

    [Fact]
    public void Filters_by_color_and_counts_what_is_shown()
    {
        var (_, filter) = Create();
        var changed = new List<string?>();
        filter.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        foreach (var toggle in filter.ColorToggles) toggle.IsShown = toggle.Color == BookmarkColor.Red;

        Assert.Equal([3L, 5], Lines(filter));
        Assert.Equal("Bookmarks (2 of 4)", filter.Header);
        Assert.Equal([BookmarkColor.Red], filter.ShownColors);
        Assert.Contains(nameof(BookmarkFilterViewModel.Items), changed);
    }

    [Fact]
    public void Notes_only_hides_bookmarks_without_a_note()
    {
        var (_, filter) = Create();

        filter.NotesOnly = true;

        Assert.Equal([1L, 5, 7], Lines(filter));
    }

    [Theory]
    [InlineData("disk", new long[] { 1, 7 })]
    [InlineData("DISK*quota", new long[] { 7 })]
    [InlineData("d*t", new long[] { 7 })]
    [InlineData("*", new long[] { 1, 3, 5, 7 })]
    [InlineData("nothing", new long[0])]
    public void Searches_the_notes_with_wildcards(string search, long[] expected)
    {
        var (_, filter) = Create();

        filter.SearchText = search;

        Assert.Equal(expected, Lines(filter));
    }

    [Fact]
    public void Follows_changes_to_the_bookmarks()
    {
        var (bookmarks, filter) = Create();
        filter.NotesOnly = true;
        var changed = new List<string?>();
        filter.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        bookmarks.SetNote(9, "new");

        Assert.Equal([1L, 5, 7, 9], Lines(filter));
        Assert.Equal("Bookmarks (4 of 5)", filter.Header);
        Assert.Contains(nameof(BookmarkFilterViewModel.Header), changed);
    }
}
