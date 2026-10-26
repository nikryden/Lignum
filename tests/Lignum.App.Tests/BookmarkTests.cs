using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Bookmarks (ticket 17): Ctrl+F2 toggles, F2 / Shift+F2 jump with wrap-around, saved per file.</summary>
public sealed class BookmarkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public BookmarkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string SettingsDir => Path.Combine(_dir, "settings");

    /// <summary>A fresh app start: every settings file is read from disk again.</summary>
    private MainViewModel Start()
    {
        var vm = UiContext.None(() => new MainViewModel(
            new SettingsStore(SettingsDir, Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(vm);
        return vm;
    }

    private string WriteLog(string name, int lines = 100)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, Enumerable.Range(0, lines).Select(i => $"line {i}"));
        return path;
    }

    private async Task<LogTabViewModel> Open(MainViewModel vm, string path)
    {
        await vm.OpenFileAsync(path);
        return vm.SelectedTab!;
    }

    [Fact]
    public async Task Double_clicking_the_margin_toggles_the_bookmark_on_that_line_not_the_caret_line()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        tab.Navigation.Caret = new FilePosition(3, 0);

        tab.ToggleBookmarkAtCommand.Execute(12L);
        Assert.Equal([12L], tab.Bookmarks.Lines);

        tab.ToggleBookmarkAtCommand.Execute(12L);
        Assert.Empty(tab.Bookmarks.Lines);
    }

    [Fact]
    public async Task A_note_is_saved_with_its_bookmark_and_restored_on_reopen()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        ToggleAt(tab, 12);
        tab.Bookmarks.SetNote(12, "  look here  ");

        var reopened = await Open(Start(), a);

        Assert.Equal("look here", reopened.Bookmarks.NoteFor(12));
        Assert.Equal([new BookmarkItem(12, "look here")], reopened.Bookmarks.Items);
    }

    [Fact]
    public async Task Clear_all_removes_every_bookmark_note_and_color_and_saves_it()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        Assert.False(tab.ClearBookmarksCommand.CanExecute(null));
        ToggleAt(tab, 3);
        tab.Bookmarks.SetColor(5, BookmarkColor.Red);
        Assert.True(tab.ClearBookmarksCommand.CanExecute(null));

        tab.ClearBookmarksCommand.Execute(null);

        Assert.Empty(tab.Bookmarks.Lines);
        Assert.Empty(tab.Bookmarks.Colors);
        Assert.False(tab.ClearBookmarksCommand.CanExecute(null));
        Assert.Empty((await Open(Start(), a)).Bookmarks.Lines);
    }

    [Fact]
    public async Task Clear_all_asks_first_when_a_bookmark_has_a_note_and_keeps_them_on_no()
    {
        var vm = Start();
        int? asked = null;
        vm.ConfirmClearBookmarks = notes => { asked = notes; return false; };
        var tab = await Open(vm, WriteLog("a.log"));
        tab.Bookmarks.SetNote(4, "keep me");
        ToggleAt(tab, 8);

        tab.ClearBookmarksCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.Equal([4L, 8L], tab.Bookmarks.Lines);
    }

    [Fact]
    public async Task A_note_on_an_unbookmarked_line_bookmarks_it()
    {
        var tab = await Open(Start(), WriteLog("a.log"));

        tab.Bookmarks.SetNote(7, "start of the run");

        Assert.Equal([7L], tab.Bookmarks.Lines);
        Assert.Equal("start of the run", tab.Bookmarks.NoteFor(7));
    }

    [Fact]
    public async Task Clearing_a_note_keeps_the_bookmark_and_removing_the_bookmark_removes_its_note()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        tab.Bookmarks.SetNote(3, "first");
        tab.Bookmarks.SetNote(9, "second");

        tab.Bookmarks.SetNote(3, "   ");
        tab.Bookmarks.Toggle(9);

        var reopened = await Open(Start(), a);
        Assert.Equal([3L], reopened.Bookmarks.Lines);
        Assert.Empty(reopened.Bookmarks.Notes);
        tab.Bookmarks.Toggle(9);
        Assert.Null(tab.Bookmarks.NoteFor(9)); // re-adding the bookmark does not bring the note back
    }

    [Fact]
    public async Task A_bookmarks_file_without_notes_loads_unchanged()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        ToggleAt(tab, 12);
        SettingsStore.Flush();
        var file = Directory.GetFiles(Path.Combine(SettingsDir, "bookmarks"), "*.json").Single();
        Assert.DoesNotContain("note", File.ReadAllText(file));

        var reopened = await Open(Start(), a);

        Assert.Equal([12L], reopened.Bookmarks.Lines);
        Assert.Empty(reopened.Bookmarks.Notes);
    }

    [Fact]
    public async Task A_line_highlight_is_saved_and_restored_without_bookmarking_the_line()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);

        tab.Bookmarks.SetHighlight([3, 4], "red", "#fff59d");
        tab.Bookmarks.SetHighlight([7], "", "#BBDEFB");

        var reopened = await Open(Start(), a);
        Assert.Empty(reopened.Bookmarks.Lines);
        Assert.Equal(new LineHighlight("#FF0000", "#FFF59D"), reopened.Bookmarks.HighlightFor(3));
        Assert.Equal(new LineHighlight("#FF0000", "#FFF59D"), reopened.Bookmarks.HighlightFor(4));
        Assert.Equal(new LineHighlight(null, "#BBDEFB"), reopened.Bookmarks.HighlightFor(7));
    }

    [Fact]
    public async Task Removing_a_highlight_or_giving_no_valid_color_clears_it_and_clear_all_bookmarks_keeps_it()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        tab.Bookmarks.SetHighlight([1, 2, 3], null, "#FFCDD2");
        ToggleAt(tab, 2);

        tab.Bookmarks.RemoveHighlight([1]);
        tab.Bookmarks.SetHighlight([3], "not a color", "");
        tab.ClearBookmarksCommand.Execute(null);

        var reopened = await Open(Start(), a);
        Assert.Equal([2L], reopened.Bookmarks.Highlights.Keys);
        Assert.Empty(reopened.Bookmarks.Lines);
    }

    [Fact]
    public async Task A_bookmarks_file_without_highlights_writes_none()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        ToggleAt(tab, 12);
        SettingsStore.Flush();

        var file = Directory.GetFiles(Path.Combine(SettingsDir, "bookmarks"), "*.json").Single();
        Assert.DoesNotContain("highlights", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_color_is_saved_with_its_bookmark_and_restored_on_reopen()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        tab.Bookmarks.SetNote(5, "slow");
        tab.Bookmarks.SetColor(5, BookmarkColor.Purple);
        tab.Bookmarks.SetColor(8, BookmarkColor.Green); // bookmarks the line too

        var reopened = await Open(Start(), a);

        Assert.Equal([5L, 8L], reopened.Bookmarks.Lines);
        Assert.Equal(
            [new BookmarkItem(5, "slow", BookmarkColor.Purple), new BookmarkItem(8, null, BookmarkColor.Green)],
            reopened.Bookmarks.Items);
    }

    [Fact]
    public async Task A_new_bookmark_is_blue_and_blue_is_not_written_to_the_file()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        ToggleAt(tab, 4);
        tab.Bookmarks.SetColor(6, BookmarkColor.Red);
        tab.Bookmarks.SetColor(6, BookmarkColor.Blue);
        SettingsStore.Flush();

        Assert.Equal(BookmarkColor.Blue, tab.Bookmarks.ColorFor(4));
        Assert.Empty(tab.Bookmarks.Colors);
        var file = Directory.GetFiles(Path.Combine(SettingsDir, "bookmarks"), "*.json").Single();
        Assert.DoesNotContain("color", File.ReadAllText(file));
    }

    [Fact]
    public async Task Removing_a_bookmark_removes_its_color()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        tab.Bookmarks.SetColor(9, BookmarkColor.Yellow);

        tab.Bookmarks.Toggle(9);
        tab.Bookmarks.Toggle(9);

        Assert.Equal(BookmarkColor.Blue, tab.Bookmarks.ColorFor(9));
    }

    [Fact]
    public async Task Clicking_a_row_in_the_bookmarks_pane_moves_the_caret_to_its_line()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        tab.Bookmarks.SetNote(20, "here");

        tab.GoToBookmarkCommand.Execute(tab.Bookmarks.Items[0]);

        Assert.Equal(20, tab.Navigation.Caret.Line);
    }

    private static void ToggleAt(LogTabViewModel tab, long line)
    {
        tab.Navigation.Caret = new FilePosition(line, 3);
        tab.ToggleBookmarkCommand.Execute(null);
    }

    [Fact]
    public async Task Toggling_the_caret_line_adds_and_removes_a_bookmark()
    {
        var tab = await Open(Start(), WriteLog("a.log"));

        ToggleAt(tab, 40);
        ToggleAt(tab, 10);
        Assert.Equal([10L, 40L], tab.Bookmarks.Lines);

        ToggleAt(tab, 40);
        Assert.Equal([10L], tab.Bookmarks.Lines);
    }

    [Fact]
    public async Task Toggle_uses_the_selection_passed_in_from_the_results_pane()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        tab.Navigation.Caret = new FilePosition(5, 0);
        var results = new Lignum.App.Viewer.TextSelection();
        results.MoveCaret(new FilePosition(77, 2), extend: false);

        tab.ToggleBookmarkCommand.Execute(results);

        Assert.Equal([77L], tab.Bookmarks.Lines);
    }

    [Fact]
    public async Task Next_and_previous_jump_between_bookmarks_and_wrap_around()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        ToggleAt(tab, 20);
        ToggleAt(tab, 60);
        ToggleAt(tab, 90);
        var reveals = 0;
        tab.Navigation.RevealRequested += (_, _) => reveals++;

        tab.Navigation.Caret = new FilePosition(30, 4);
        tab.NextBookmarkCommand.Execute(null);
        Assert.Equal(new FilePosition(60, 0), tab.Navigation.Caret);
        tab.NextBookmarkCommand.Execute(null);
        Assert.Equal(90, tab.Navigation.Caret.Line);
        tab.NextBookmarkCommand.Execute(null); // wraps to the first
        Assert.Equal(20, tab.Navigation.Caret.Line);

        tab.PreviousBookmarkCommand.Execute(null); // wraps to the last
        Assert.Equal(90, tab.Navigation.Caret.Line);
        tab.PreviousBookmarkCommand.Execute(null);
        Assert.Equal(60, tab.Navigation.Caret.Line);
        tab.Navigation.Caret = new FilePosition(61, 0);
        tab.PreviousBookmarkCommand.Execute(null);
        Assert.Equal(60, tab.Navigation.Caret.Line);
        Assert.Equal(6, reveals); // every jump scrolls the view to the caret
    }

    [Fact]
    public async Task A_single_bookmark_is_reached_from_its_own_line_by_wrapping()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        ToggleAt(tab, 50);

        tab.NextBookmarkCommand.Execute(null);
        Assert.Equal(50, tab.Navigation.Caret.Line);
        tab.PreviousBookmarkCommand.Execute(null);
        Assert.Equal(50, tab.Navigation.Caret.Line);
    }

    [Fact]
    public async Task Next_and_previous_do_nothing_without_bookmarks()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        tab.Navigation.Caret = new FilePosition(12, 3);

        tab.NextBookmarkCommand.Execute(null);
        tab.PreviousBookmarkCommand.Execute(null);

        Assert.Equal(new FilePosition(12, 3), tab.Navigation.Caret);
    }

    [Fact]
    public async Task Bookmarks_past_the_lines_indexed_are_skipped_when_jumping()
    {
        var path = WriteLog("a.log");
        var vm = Start();
        var tab = await Open(vm, path);
        ToggleAt(tab, 10);
        ToggleAt(tab, 95);
        vm.Dispose();
        File.WriteAllLines(path, Enumerable.Range(0, 50).Select(i => $"short {i}")); // the file shrank

        var reopened = await Open(Start(), path);
        Assert.Equal([10L, 95L], reopened.Bookmarks.Lines); // kept, in case the file grows back
        reopened.Navigation.Caret = new FilePosition(20, 0);
        reopened.NextBookmarkCommand.Execute(null);
        Assert.Equal(10, reopened.Navigation.Caret.Line);
    }

    [Fact]
    public async Task Bookmarks_are_saved_per_file_and_restored_when_it_is_reopened()
    {
        var a = WriteLog("a.log");
        var b = WriteLog("b.log");
        var vm = Start();
        var tabA = await Open(vm, a);
        ToggleAt(tabA, 3);
        ToggleAt(tabA, 33);
        var tabB = await Open(vm, b);
        ToggleAt(tabB, 7);
        vm.Dispose();

        var restarted = Start();
        // A different spelling of the same path finds the same bookmarks (Windows paths ignore case).
        var reopenedA = await Open(restarted, a.ToUpperInvariant());
        var reopenedB = await Open(restarted, b);

        Assert.Equal([3L, 33L], reopenedA.Bookmarks.Lines);
        Assert.Equal([7L], reopenedB.Bookmarks.Lines);
        Assert.Empty((await Open(restarted, WriteLog("c.log"))).Bookmarks.Lines);
    }

    [Fact]
    public async Task Each_file_gets_its_own_json_file_with_a_list_of_bookmark_objects()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        ToggleAt(tab, 12);

        SettingsStore.Flush(); // saves write in the background
        var file = Assert.Single(Directory.GetFiles(Path.Combine(SettingsDir, "bookmarks"), "*.json"));
        var json = File.ReadAllText(file);
        // Objects, not bare numbers, so a "note" can be added to each bookmark later without a migration.
        Assert.Matches("\"bookmarks\":\\s*\\[\\s*\\{\\s*\"line\":\\s*12\\s*\\}\\s*\\]", json);
        Assert.Contains("a.log", json); // the path is kept for people reading the folder
        Assert.DoesNotContain("a.log", Path.GetFileName(file)); // the file name is a hash of the path
    }

    [Fact]
    public async Task Unknown_fields_on_a_bookmark_such_as_a_future_note_survive_a_save()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        ToggleAt(tab, 12);
        SettingsStore.Flush();
        var file = Directory.GetFiles(Path.Combine(SettingsDir, "bookmarks"), "*.json").Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"line\": 12", "\"line\": 12, \"note\": \"look here\""));

        var reopened = await Open(Start(), a);
        Assert.Equal([12L], reopened.Bookmarks.Lines);
        ToggleAt(reopened, 40); // saves again

        SettingsStore.Flush();
        Assert.Contains("\"note\": \"look here\"", File.ReadAllText(file));
    }

    [Fact]
    public async Task A_corrupt_bookmarks_file_gives_no_bookmarks()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        ToggleAt(tab, 12);
        SettingsStore.Flush();
        var file = Directory.GetFiles(Path.Combine(SettingsDir, "bookmarks"), "*.json").Single();
        File.WriteAllText(file, "{ not json");

        var reopened = await Open(Start(), a);

        Assert.Empty(reopened.Bookmarks.Lines);
    }

    // Ticket 24: bulk removal by note and color, and showing only some colors in the margin.

    [Fact]
    public async Task Remove_without_notes_keeps_only_bookmarks_with_a_note_and_saves_it()
    {
        var a = WriteLog("a.log");
        var tab = await Open(Start(), a);
        ToggleAt(tab, 2);
        tab.Bookmarks.SetNote(5, "keep");
        tab.Bookmarks.SetColor(8, BookmarkColor.Red);

        tab.RemoveBookmarksWithoutNotesCommand.Execute(null);

        Assert.Equal([5L], tab.Bookmarks.Lines);
        Assert.Empty(tab.Bookmarks.Colors);
        Assert.Equal([5L], (await Open(Start(), a)).Bookmarks.Lines);
    }

    [Fact]
    public async Task Remove_of_color_removes_only_that_color_and_asks_when_one_has_a_note()
    {
        var vm = Start();
        var answer = false;
        int? asked = null;
        vm.ConfirmClearBookmarks = notes => { asked = notes; return answer; };
        var tab = await Open(vm, WriteLog("a.log"));
        ToggleAt(tab, 1);
        tab.Bookmarks.SetColor(3, BookmarkColor.Red);
        tab.Bookmarks.SetColor(4, BookmarkColor.Red);
        tab.Bookmarks.SetNote(4, "why");
        tab.Bookmarks.SetColor(6, BookmarkColor.Green);

        tab.RemoveBookmarksOfColorCommand.Execute(BookmarkColor.Red);
        Assert.Equal(1, asked);
        Assert.Equal([1L, 3, 4, 6], tab.Bookmarks.Lines); // cancelled

        answer = true;
        tab.RemoveBookmarksOfColorCommand.Execute(BookmarkColor.Red);
        Assert.Equal([1L, 6], tab.Bookmarks.Lines);
        Assert.Null(tab.Bookmarks.NoteFor(4));

        asked = null;
        tab.RemoveBookmarksOfColorCommand.Execute(BookmarkColor.Blue);
        Assert.Null(asked); // no notes, no question
        Assert.Equal([6L], tab.Bookmarks.Lines);
    }

    [Fact]
    public async Task Showing_only_some_colors_hides_the_other_markers_and_F2_skips_them()
    {
        var tab = await Open(Start(), WriteLog("a.log"));
        ToggleAt(tab, 2);
        tab.Bookmarks.SetColor(5, BookmarkColor.Red);
        ToggleAt(tab, 9);
        Assert.Same(tab.Bookmarks.Lines, tab.BookmarkFilter.VisibleLines);

        tab.BookmarkFilter.ColorToggles.Single(t => t.Color == BookmarkColor.Blue).IsShown = false;

        Assert.Equal([5L], tab.BookmarkFilter.VisibleLines);
        Assert.Equal([2L, 5, 9], tab.Bookmarks.Lines); // hidden, not removed
        tab.Navigation.Caret = new FilePosition(0, 0);
        tab.NextBookmarkCommand.Execute(null);
        Assert.Equal(5, tab.Navigation.Caret.Line);
        tab.NextBookmarkCommand.Execute(null);
        Assert.Equal(5, tab.Navigation.Caret.Line); // wraps to itself
        tab.PreviousBookmarkCommand.Execute(null);
        Assert.Equal(5, tab.Navigation.Caret.Line);
    }
}
