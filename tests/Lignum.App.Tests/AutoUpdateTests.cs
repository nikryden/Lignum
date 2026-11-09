using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Ticket 27: a tab can update by itself when its file changes.</summary>
public class AutoUpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public AutoUpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private MainViewModel Create() => UiContext.None(() =>
        new MainViewModel(new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));

    private string WriteLog(string content)
    {
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, content);
        return path;
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the view model.");
            Thread.Sleep(20);
        }
    }

    private async Task<(MainViewModel Main, LogTabViewModel Tab, string Path)> OpenAsync(string content)
    {
        var main = Create();
        var path = WriteLog(content);
        await main.OpenFileAsync(path);
        return (main, main.SelectedTab!, path);
    }

    private static void Truncate(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        stream.SetLength(length);
    }

    [Fact]
    public async Task Auto_update_is_off_by_default_and_stays_in_place()
    {
        var (main, tab, _) = await OpenAsync("a\n");
        using var _ = main;

        Assert.False(tab.AutoUpdate);
        Assert.Equal(UpdatePosition.StayInPlace, tab.UpdatePosition);
    }

    [Fact]
    public async Task With_auto_update_growth_adds_the_lines_without_a_banner()
    {
        var (main, tab, path) = await OpenAsync("a\nb\n");
        using var _ = main;
        tab.AutoUpdate = true;

        File.AppendAllText(path, "c\nd\n");

        WaitUntil(() => tab.LineCount == 4);
        Assert.False(tab.IsFileChanged);
        Assert.Equal(new FileInfo(path).Length, tab.FileSize);
    }

    [Fact]
    public async Task Turning_auto_update_on_applies_the_change_the_banner_offers()
    {
        var (main, tab, path) = await OpenAsync("a\n");
        using var _ = main;
        File.AppendAllText(path, "b\n");
        WaitUntil(() => tab.IsFileChanged);

        tab.AutoUpdate = true;

        WaitUntil(() => tab.LineCount == 2);
        Assert.False(tab.IsFileChanged);
    }

    [Fact]
    public async Task Reload_of_a_grown_file_adds_the_new_lines()
    {
        var (main, tab, path) = await OpenAsync("a\n");
        using var _ = main;
        var document = tab.Document;
        File.AppendAllText(path, "b\n");
        WaitUntil(() => tab.IsFileChanged);

        await tab.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(2, tab.LineCount);
        Assert.Same(document, tab.Document); // extended, not reopened
        Assert.False(tab.IsFileChanged);
    }

    [Fact]
    public async Task With_auto_update_truncation_reloads_the_tab()
    {
        var (main, tab, path) = await OpenAsync("aa\nbb\ncc\n");
        using var _ = main;
        var document = tab.Document;
        tab.AutoUpdate = true;

        Truncate(path, 3);

        WaitUntil(() => !ReferenceEquals(document, tab.Document) && tab.LineCount == 1 && !tab.IsIndexing);
        Assert.False(tab.IsFileChanged);
        Assert.Equal(["aa"], tab.Document!.ReadLines(0, 10));
    }

    [Fact]
    public async Task Jump_to_last_line_moves_the_caret_to_the_end_on_each_update()
    {
        var (main, tab, path) = await OpenAsync("a\nb\n");
        using var _ = main;
        tab.UpdatePosition = UpdatePosition.JumpToEnd;
        tab.AutoUpdate = true;

        File.AppendAllText(path, "c\nd\n");

        WaitUntil(() => tab.Navigation.Caret.Line == 3);
    }

    [Fact]
    public async Task Stay_in_place_follows_the_new_lines_when_the_last_line_is_in_view()
    {
        var (main, tab, path) = await OpenAsync("a\nb\n");
        using var _ = main;
        tab.ReportViewportRows(10); // the whole file fits
        var scrolledToEnd = 0;
        tab.ScrollToEndRequested += (_, _) => Interlocked.Increment(ref scrolledToEnd);
        tab.AutoUpdate = true;

        File.AppendAllText(path, "c\n");

        WaitUntil(() => Volatile.Read(ref scrolledToEnd) > 0);
        Assert.Equal(0, tab.Navigation.Caret.Line); // following scrolls; it does not move the caret
    }

    [Fact]
    public async Task Stay_in_place_keeps_the_view_when_the_last_line_is_not_in_view()
    {
        var (main, tab, path) = await OpenAsync(string.Concat(Enumerable.Range(0, 100).Select(i => $"{i}\n")));
        using var _ = main;
        tab.ReportViewportRows(10);
        tab.ReportScroll(20); // lines 20-29 in view
        var scrolledToEnd = false;
        tab.ScrollToEndRequested += (_, _) => scrolledToEnd = true;
        tab.AutoUpdate = true;

        File.AppendAllText(path, "more\n");

        WaitUntil(() => tab.LineCount == 101);
        Thread.Sleep(50); // let a (wrong) follow land, if any
        Assert.False(scrolledToEnd);
        Assert.Equal(20, tab.ScrollLine);
        Assert.Equal(0, tab.Navigation.Caret.Line);
    }

    [Fact]
    public async Task Stay_in_place_keeps_the_view_when_the_file_is_rewritten()
    {
        var lines = string.Concat(Enumerable.Range(0, 100).Select(i => $"line {i}\n"));
        var (main, tab, path) = await OpenAsync(lines);
        using var _ = main;
        tab.ReportViewportRows(10);
        tab.ReportScroll(20); // lines 20-29 in view, far from the end
        var document = tab.Document;
        var scrolledToEnd = false;
        tab.ScrollToEndRequested += (_, _) => scrolledToEnd = true;
        tab.AutoUpdate = true;

        // Rewritten in place and longer: not an append, so the tab reloads.
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            writer.Write(System.Text.Encoding.UTF8.GetBytes("new " + lines + "more\n"));

        WaitUntil(() => !ReferenceEquals(document, tab.Document) && !tab.IsIndexing && tab.LineCount == 101);
        Thread.Sleep(50); // let a (wrong) follow land, if any
        Assert.False(scrolledToEnd);
        Assert.Equal(20, tab.ScrollLine);
        Assert.NotEqual(100, tab.Navigation.Caret.Line);
    }

    [Fact]
    public async Task The_tab_icon_shows_needs_update_or_auto_updating()
    {
        var (main, tab, path) = await OpenAsync("a\n");
        using var _ = main;
        Assert.Equal(TabIndicator.None, tab.Indicator);

        File.AppendAllText(path, "b\n");
        WaitUntil(() => tab.Indicator == TabIndicator.NeedsUpdate);

        tab.AutoUpdate = true;
        WaitUntil(() => tab.Indicator == TabIndicator.AutoUpdating && tab.LineCount == 2);

        tab.AutoUpdate = false;
        Assert.Equal(TabIndicator.None, tab.Indicator);
    }

    [Fact]
    public async Task The_menu_radio_items_follow_the_update_position()
    {
        var (main, tab, _) = await OpenAsync("a\n");
        using var _ = main;
        Assert.True(tab.StaysInPlace);

        tab.JumpsToEnd = true;
        Assert.Equal(UpdatePosition.JumpToEnd, tab.UpdatePosition);
        Assert.False(tab.StaysInPlace);

        tab.StaysInPlace = true;
        Assert.Equal(UpdatePosition.StayInPlace, tab.UpdatePosition);
    }

    [Fact]
    public async Task Results_are_outdated_after_growth_until_run_again()
    {
        var (main, tab, path) = await OpenAsync("error 1\ninfo\n");
        using var _ = main;
        tab.Search.FindText = "error";
        await tab.Search.FindAllCommand.ExecuteAsync(null);
        Assert.Equal(1, tab.Search.MatchCount);
        tab.AutoUpdate = true;

        File.AppendAllText(path, "error 2\n");
        WaitUntil(() => tab.Search.IsOutdated);
        Assert.Equal(1, tab.Search.MatchCount); // not extended

        await tab.Search.RunAgainCommand.ExecuteAsync(null);
        Assert.False(tab.Search.IsOutdated);
        Assert.Equal(2, tab.Search.MatchCount);
    }
}
