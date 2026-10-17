using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Session restore (ticket 18): open tabs, scroll positions and filter rules survive a restart.</summary>
public sealed class SessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public SessionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A fresh app start: settings.json is read from disk again.</summary>
    private MainViewModel Start()
    {
        var vm = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        _owned.Add(vm);
        return vm;
    }

    private string WriteLog(string name, int lines = 100)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, Enumerable.Range(0, lines).Select(i => $"line {i}"));
        return path;
    }

    /// <summary>Exit: save the session, then close every tab (as the app does).</summary>
    private static void Exit(MainViewModel vm)
    {
        vm.SaveSession();
        vm.Dispose();
    }

    [Fact]
    public async Task Tabs_scroll_positions_and_filters_are_restored_after_a_restart()
    {
        var a = WriteLog("a.log");
        var b = WriteLog("b.log");
        var c = WriteLog("c.log");
        var vm = Start();
        await vm.OpenFilesAsync([a, b, c]);
        vm.Tabs[0].ReportScroll(42);
        vm.Tabs[1].Search.AddFilterRule(new QueryRule("line 1", RuleKind.Include));
        vm.Tabs[1].Search.AddFilterRule(new QueryRule("line 10", RuleKind.Exclude, IsRegex: true, IsEnabled: false));
        vm.SelectedTab = vm.Tabs[1];
        Exit(vm);

        var restarted = Start();
        await restarted.RestoreSession();

        Assert.Equal([a, b, c], restarted.Tabs.Select(t => t.Path));
        Assert.Same(restarted.Tabs[1], restarted.SelectedTab);
        Assert.Equal(42, restarted.Tabs[0].ScrollLine);
        Assert.Equal(0, restarted.Tabs[2].ScrollLine);
        Assert.Equal(
            [new QueryRule("line 1", RuleKind.Include), new QueryRule("line 10", RuleKind.Exclude, IsRegex: true, IsEnabled: false)],
            restarted.Tabs[1].Search.GetFilterRules());
        Assert.Empty(restarted.Tabs[0].Search.GetFilterRules());
        Assert.Equal(100, restarted.Tabs[0].LineCount); // re-indexed
        Assert.Null(restarted.ErrorMessage);
    }

    [Fact]
    public async Task Restored_filter_rules_run_on_the_reindexed_file()
    {
        var a = WriteLog("a.log", lines: 20);
        var vm = Start();
        await vm.OpenFileAsync(a);
        vm.SelectedTab!.Search.AddFilterRule(new QueryRule("line 1"));
        Exit(vm);

        var restarted = Start();
        await restarted.RestoreSession();
        var search = restarted.SelectedTab!.Search;
        await search.ActiveQuery!.Completion;

        Assert.Equal(11, search.MatchCount); // line 1, line 10..19
    }

    [Fact]
    public async Task Files_that_no_longer_exist_are_skipped_with_a_message()
    {
        var a = WriteLog("a.log");
        var gone = WriteLog("gone.log");
        var vm = Start();
        await vm.OpenFilesAsync([gone, a]);
        Exit(vm);
        File.Delete(gone);

        var restarted = Start();
        await restarted.RestoreSession();

        Assert.Equal([a], restarted.Tabs.Select(t => t.Path));
        Assert.Same(restarted.Tabs[0], restarted.SelectedTab);
        Assert.Contains("gone.log", restarted.ErrorMessage);
    }

    [Fact]
    public async Task Restore_reopens_the_tabs_with_their_scroll_position_pending()
    {
        var a = WriteLog("a.log");
        var vm = Start();
        await vm.OpenFileAsync(a);
        vm.SelectedTab!.ReportScroll(7);
        Exit(vm);

        var restarted = Start();
        await restarted.RestoreSession(); // files open in the background, so the tabs only appear later

        var tab = Assert.Single(restarted.Tabs);
        Assert.True(tab.IsScrollRestorePending);
        Assert.Equal(7, tab.ScrollLine);
    }

    [Fact]
    public async Task A_pending_scroll_restore_is_kept_until_the_view_reaches_it()
    {
        var a = WriteLog("a.log");
        var vm = Start();
        await vm.OpenFileAsync(a);
        vm.SelectedTab!.ReportScroll(30);
        Exit(vm);

        var restarted = Start();
        await restarted.RestoreSession();
        var tab = restarted.SelectedTab!;
        tab.ReportScroll(0); // the view starts at the top before it has enough rows
        Assert.Equal(30, tab.ScrollLine);

        tab.CompleteScrollRestore();
        tab.ReportScroll(12);
        Assert.False(tab.IsScrollRestorePending);
        Assert.Equal(12, tab.ScrollLine);
    }

    [Fact]
    public async Task A_session_saved_with_no_tabs_restores_nothing()
    {
        var vm = Start();
        await vm.OpenFileAsync(WriteLog("a.log"));
        vm.CloseTabCommand.Execute(null);
        Exit(vm);

        var restarted = Start();
        await restarted.RestoreSession();

        Assert.Empty(restarted.Tabs);
        Assert.Null(restarted.SelectedTab);
        Assert.Null(restarted.ErrorMessage);
    }

    [Fact]
    public async Task Restoring_does_not_reorder_the_recent_files()
    {
        var a = WriteLog("a.log");
        var b = WriteLog("b.log");
        var vm = Start();
        await vm.OpenFilesAsync([a, b]);
        await vm.OpenFileAsync(a); // selects a's tab and moves it to the top of the recent files
        Exit(vm);

        var restarted = Start();
        await restarted.RestoreSession();

        Assert.Equal([a, b], restarted.RecentFiles);
    }
}
