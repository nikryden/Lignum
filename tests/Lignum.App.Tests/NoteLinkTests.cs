using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Links in bookmark notes and editing the note in the pane (ticket 23).</summary>
public sealed class NoteLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public NoteLinkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string[] Targets(string note) => [.. NoteLinks.Find(note).Select(l => l.Target)];

    [Theory]
    [InlineData("see https://example.com/a?b=1 now", "https://example.com/a?b=1")]
    [InlineData("ticket http://jira/X-1.", "http://jira/X-1")]
    [InlineData(@"in C:\logs\app.log, line 4", @"C:\logs\app.log")]
    [InlineData(@"share \\server\logs\run 2 failed", @"\\server\logs\run")]
    [InlineData(@"(see D:\data\)", @"D:\data\")]
    [InlineData(@"quoted ""C:\My logs\a b.log"" here", @"C:\My logs\a b.log")]
    public void Finds_web_addresses_and_paths_without_trailing_punctuation(string note, string expected) =>
        Assert.Equal([expected], Targets(note));

    [Fact]
    public void Finds_several_links_in_order_with_their_positions()
    {
        const string note = @"https://a.example and C:\x.log";

        var links = NoteLinks.Find(note);

        Assert.Equal([NoteLinkKind.Web, NoteLinkKind.Path], links.Select(l => l.Kind));
        Assert.Equal(@"C:\x.log", note.Substring(links[1].Start, links[1].Length));
        Assert.Equal(links[1], NoteLinks.At(note, links[1].Start + 2));
        Assert.Null(NoteLinks.At(note, note.IndexOf("and", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("no links here")]
    [InlineData("time 12:30:45")]
    [InlineData("ftp://not.supported")]
    [InlineData("")]
    public void Plain_text_has_no_links(string note) => Assert.Empty(Targets(note));

    private MainViewModel Start(List<ProcessStartInfo> started)
    {
        var vm = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        vm.StartProcess = started.Add;
        _owned.Add(vm);
        return vm;
    }

    private static NoteLink Link(string target) => NoteLinks.Find(target).Single();

    [Fact]
    public async Task A_web_link_opens_in_the_default_browser()
    {
        var started = new List<ProcessStartInfo>();
        var vm = Start(started);

        await vm.OpenNoteLinkAsync(Link("https://example.com"));

        Assert.Equal("https://example.com", Assert.Single(started).FileName);
        Assert.True(started[0].UseShellExecute);
    }

    [Fact]
    public async Task A_file_link_opens_in_a_tab_and_show_in_folder_selects_it_in_Explorer()
    {
        var started = new List<ProcessStartInfo>();
        var vm = Start(started);
        var log = Path.Combine(_dir, "linked.log");
        File.WriteAllLines(log, ["a", "b"]);

        await vm.OpenNoteLinkAsync(Link(log));
        Assert.Equal("linked.log", vm.SelectedTab!.FileName);
        Assert.Empty(started);

        await vm.OpenNoteLinkAsync(Link(log), inFolder: true);
        Assert.Equal("explorer.exe", Assert.Single(started).FileName);
        Assert.Equal($"/select,\"{log}\"", started[0].Arguments);
    }

    [Fact]
    public async Task A_folder_link_opens_in_Explorer()
    {
        var started = new List<ProcessStartInfo>();
        var vm = Start(started);

        await vm.OpenNoteLinkAsync(Link(_dir));

        Assert.Equal(_dir, Assert.Single(started).FileName);
        Assert.Empty(vm.Tabs);
    }

    [Fact]
    public async Task A_missing_path_is_reported_when_clicked()
    {
        var started = new List<ProcessStartInfo>();
        var vm = Start(started);
        var missing = Path.Combine(_dir, "gone.log");

        await vm.OpenNoteLinkAsync(Link(missing));

        Assert.Empty(started);
        Assert.Empty(vm.Tabs);
        Assert.Equal($"{missing} does not exist.", vm.ErrorMessage);
    }

    [Fact]
    public void The_pane_edits_the_selected_bookmarks_note_and_keeps_the_selection()
    {
        var bookmarks = new BookmarksViewModel(null, @"C:\logs\a.log");
        bookmarks.SetNote(2, "old");
        bookmarks.Toggle(6);
        var filter = new BookmarkFilterViewModel(bookmarks);
        Assert.False(filter.HasSelection);

        filter.SelectedItem = filter.Items[0];
        filter.SelectedNote = "new note";
        filter.SelectedItem = null; // the list does this while it swaps its items

        Assert.Equal("new note", bookmarks.NoteFor(2));
        Assert.Equal(2L, filter.SelectedItem?.Line);
        Assert.Equal("new note", filter.SelectedNote);

        filter.SelectedNote = "";
        Assert.Null(bookmarks.NoteFor(2));
        Assert.Contains(2L, bookmarks.Lines); // clearing the note keeps the bookmark
    }

    [Fact]
    public void The_selection_goes_when_its_bookmark_is_removed_or_hidden()
    {
        var bookmarks = new BookmarksViewModel(null, @"C:\logs\a.log");
        bookmarks.Toggle(2);
        bookmarks.SetNote(6, "has a note");
        var filter = new BookmarkFilterViewModel(bookmarks);
        filter.SelectedItem = filter.Items[0];

        filter.NotesOnly = true;
        Assert.False(filter.HasSelection);
        filter.NotesOnly = false;
        Assert.Equal(2L, filter.SelectedItem?.Line); // hidden, not forgotten

        bookmarks.Toggle(2);
        bookmarks.Toggle(2);
        Assert.False(filter.HasSelection); // removed: forgotten
    }
}
