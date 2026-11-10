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

    private static string[] Paths(TabGroupViewModel group) => [.. group.Tabs.Select(t => t.Path)];

    [Fact]
    public async Task Floating_windows_their_tabs_order_shown_tab_and_bounds_are_restored()
    {
        var (a, b, c, d) = (WriteLog("a.log"), WriteLog("b.log"), WriteLog("c.log"), WriteLog("d.log"));
        var vm = Start();
        await vm.OpenFilesAsync([a, b, c, d]);
        var floating = vm.MoveToNewGroup(vm.Tabs[1])!;
        vm.MoveTab(vm.Tabs[2], floating, 0); // c, b
        floating.Bounds = new WindowBounds { Left = 10, Top = 20, Width = 600, Height = 400 };
        floating.IsMaximized = true;
        floating.SelectedTab = floating.Tabs[1]; // b
        vm.MainGroup.SelectedTab = vm.MainGroup.Tabs[0]; // a
        vm.Tabs[3].ReportScroll(7);
        Exit(vm);

        var restarted = Start();
        await restarted.RestoreSession();

        Assert.Equal([a, d], Paths(restarted.MainGroup));
        var group = Assert.Single(restarted.FloatingGroups);
        Assert.Equal([c, b], Paths(group));
        Assert.Equal(b, group.SelectedTab!.Path);
        Assert.Equal(a, restarted.MainGroup.SelectedTab!.Path);
        Assert.Same(restarted.MainGroup, restarted.ActiveGroup);
        Assert.Equal(a, restarted.SelectedTab!.Path);
        Assert.Equal((10d, 20d, 600d, 400d), (group.Bounds!.Left, group.Bounds.Top, group.Bounds.Width, group.Bounds.Height));
        Assert.True(group.IsMaximized);
        Assert.Equal(7, restarted.Tabs.Single(t => t.Path == d).ScrollLine);
    }

    [Fact]
    public async Task A_floating_window_whose_files_are_gone_is_not_reopened()
    {
        var (a, b) = (WriteLog("a.log"), WriteLog("b.log"));
        var vm = Start();
        await vm.OpenFilesAsync([a, b]);
        vm.MoveToNewGroup(vm.Tabs[1]);
        Exit(vm);
        File.Delete(b);

        var restarted = Start();
        await restarted.RestoreSession();

        Assert.Empty(restarted.FloatingGroups);
        Assert.Equal([a], Paths(restarted.MainGroup));
        Assert.Contains("b.log", restarted.ErrorMessage);
    }

    [Fact]
    public async Task Tabs_of_an_unknown_window_and_old_settings_without_windows_open_in_the_main_window()
    {
        var (a, b) = (WriteLog("a.log"), WriteLog("b.log"));
        var settings = new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName);
        settings.Value.Session = new SessionSettings
        {
            Tabs = [new SessionTab { Path = a }, new SessionTab { Path = b, Window = 3 }],
            SelectedIndex = 1,
            Windows = null!, // an older settings.json
        };
        settings.Save();
        SettingsStore.Flush();

        var restarted = Start();
        await restarted.RestoreSession();

        Assert.Empty(restarted.FloatingGroups);
        Assert.Equal([a, b], Paths(restarted.MainGroup));
        Assert.Equal(b, restarted.SelectedTab!.Path);
    }
}
