using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Docking and undocking tabs: tab groups, moves and the active tab (ticket 29).</summary>
public sealed class TabWindowsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public TabWindowsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private MainViewModel Create() => UiContext.None(() =>
        new MainViewModel(new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));

    /// <summary>Opens a.log, b.log, c.log (…) in the main window; the last one is active.</summary>
    private async Task<MainViewModel> CreateWithTabs(params string[] names)
    {
        var vm = Create();
        foreach (var name in names)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, "1\n2\n3\n");
            await vm.OpenFileAsync(path);
        }
        return vm;
    }

    private static string[] Names(TabGroupViewModel group) => [.. group.Tabs.Select(t => t.FileName)];

    private static LogTabViewModel Tab(MainViewModel vm, string name) => vm.Tabs.Single(t => t.FileName == name);

    [Fact]
    public async Task Opened_files_go_to_the_main_window()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");

        Assert.Equal(["a.log", "b.log"], Names(vm.MainGroup));
        Assert.Same(vm.MainGroup, vm.ActiveGroup);
        Assert.Same(vm.MainGroup.SelectedTab, vm.SelectedTab);
        Assert.Empty(vm.FloatingGroups);
    }

    [Fact]
    public async Task A_tab_reorders_inside_its_window()
    {
        using var vm = await CreateWithTabs("a.log", "b.log", "c.log");

        vm.MoveTab(Tab(vm, "c.log"), vm.MainGroup, 0);
        Assert.Equal(["c.log", "a.log", "b.log"], Names(vm.MainGroup));

        vm.MoveTab(Tab(vm, "c.log"), vm.MainGroup, 99); // clamped to the end
        Assert.Equal(["a.log", "b.log", "c.log"], Names(vm.MainGroup));
        Assert.Equal(3, vm.Tabs.Count);
    }

    [Fact]
    public async Task Moving_to_a_new_window_makes_a_floating_group_and_the_tab_active()
    {
        using var vm = await CreateWithTabs("a.log", "b.log", "c.log");
        var b = Tab(vm, "b.log");

        var group = vm.MoveToNewGroup(b);

        Assert.NotNull(group);
        Assert.Equal([group], vm.FloatingGroups);
        Assert.Equal(["b.log"], Names(group));
        Assert.Equal(["a.log", "c.log"], Names(vm.MainGroup));
        Assert.Same(b, vm.SelectedTab);
        Assert.Same(group, vm.ActiveGroup);
        Assert.True(b.IsSelected);
        Assert.Same(vm.MainGroup.SelectedTab, Tab(vm, "c.log")); // the main window still shows its tab
        Assert.True(Tab(vm, "c.log").IsSelected);
        Assert.Equal("b.log - Lignum", group.Title);
        Assert.Equal(3, vm.Tabs.Count); // still open, in Tabs
    }

    [Fact]
    public async Task Moving_the_shown_tab_out_shows_its_neighbour()
    {
        using var vm = await CreateWithTabs("a.log", "b.log", "c.log");
        vm.SelectedTab = Tab(vm, "b.log");

        vm.MoveToNewGroup(Tab(vm, "b.log"));

        Assert.Same(Tab(vm, "c.log"), vm.MainGroup.SelectedTab);
        Assert.False(Tab(vm, "a.log").IsSelected);
    }

    [Fact]
    public async Task A_tab_alone_in_a_floating_window_cannot_move_to_a_new_window()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");
        var b = Tab(vm, "b.log");

        Assert.True(vm.MoveToNewWindowCommand.CanExecute(b));
        Assert.False(vm.MoveToMainWindowCommand.CanExecute(b));

        vm.MoveToNewGroup(b);

        Assert.False(vm.MoveToNewWindowCommand.CanExecute(b));
        Assert.True(vm.MoveToMainWindowCommand.CanExecute(b));
        Assert.Null(vm.MoveToNewGroup(b));
        Assert.Single(vm.FloatingGroups);
    }

    [Fact]
    public async Task Tabs_move_between_floating_windows_and_an_emptied_window_closes()
    {
        using var vm = await CreateWithTabs("a.log", "b.log", "c.log");
        var first = vm.MoveToNewGroup(Tab(vm, "a.log"))!;
        var second = vm.MoveToNewGroup(Tab(vm, "b.log"))!;

        vm.MoveTab(Tab(vm, "b.log"), first, 0);

        Assert.Equal(["b.log", "a.log"], Names(first));
        Assert.Equal([first], vm.FloatingGroups);
        Assert.Same(first, vm.ActiveGroup);
        Assert.Same(Tab(vm, "b.log"), first.SelectedTab);
        Assert.False(Tab(vm, "a.log").IsSelected);
        Assert.Empty(second.Tabs);
    }

    [Fact]
    public async Task Docking_moves_the_tab_to_the_end_of_the_main_window()
    {
        using var vm = await CreateWithTabs("a.log", "b.log", "c.log");
        vm.MoveToNewGroup(Tab(vm, "a.log"));

        vm.MoveToMainWindowCommand.Execute(Tab(vm, "a.log"));

        Assert.Equal(["b.log", "c.log", "a.log"], Names(vm.MainGroup));
        Assert.Empty(vm.FloatingGroups);
        Assert.Same(vm.MainGroup, vm.ActiveGroup);
        Assert.Same(Tab(vm, "a.log"), vm.SelectedTab);
    }

    [Fact]
    public async Task Closing_a_floating_window_docks_its_tabs_in_order()
    {
        using var vm = await CreateWithTabs("a.log", "b.log", "c.log");
        var group = vm.MoveToNewGroup(Tab(vm, "a.log"))!;
        vm.MoveTab(Tab(vm, "b.log"), group, 1);
        vm.SelectedTab = Tab(vm, "a.log");

        vm.CloseFloatingGroup(group);

        Assert.Empty(vm.FloatingGroups);
        Assert.Equal(["c.log", "a.log", "b.log"], Names(vm.MainGroup));
        Assert.Same(Tab(vm, "a.log"), vm.MainGroup.SelectedTab);
        Assert.Same(vm.MainGroup, vm.ActiveGroup);
        Assert.Same(Tab(vm, "a.log"), vm.SelectedTab);
        Assert.Equal(3, vm.Tabs.Count);
    }

    [Fact]
    public async Task Closing_the_last_tab_of_a_floating_window_closes_it_and_the_main_window_becomes_active()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");
        vm.MoveToNewGroup(Tab(vm, "b.log"));

        vm.CloseTabCommand.Execute(null); // the active tab: b.log

        Assert.Empty(vm.FloatingGroups);
        Assert.Equal(["a.log"], vm.Tabs.Select(t => t.FileName));
        Assert.Same(vm.MainGroup, vm.ActiveGroup);
        Assert.Same(Tab(vm, "a.log"), vm.SelectedTab);
    }

    [Fact]
    public async Task Activating_a_window_makes_its_shown_tab_active_and_new_files_open_there()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");
        var group = vm.MoveToNewGroup(Tab(vm, "b.log"))!;

        vm.ActivateGroup(vm.MainGroup);
        Assert.Same(Tab(vm, "a.log"), vm.SelectedTab);

        vm.ActivateGroup(group);
        Assert.Same(Tab(vm, "b.log"), vm.SelectedTab);

        var path = Path.Combine(_dir, "c.log");
        File.WriteAllText(path, "1\n");
        await vm.OpenFileAsync(path);

        Assert.Equal(["b.log", "c.log"], Names(group));
        Assert.Same(Tab(vm, "c.log"), vm.SelectedTab);
    }

    [Fact]
    public async Task Selecting_a_tab_in_another_window_activates_that_window()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");
        var group = vm.MoveToNewGroup(Tab(vm, "b.log"))!;

        vm.SelectedTab = Tab(vm, "a.log"); // e.g. a search result in a.log, or reopening a.log
        Assert.Same(vm.MainGroup, vm.ActiveGroup);

        group.SelectedTab = Tab(vm, "b.log"); // already shown there: not the active window, so nothing changes
        Assert.Same(Tab(vm, "a.log"), vm.SelectedTab);
    }

    [Fact]
    public async Task A_moved_tab_scrolls_back_to_where_it_was()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");
        var a = Tab(vm, "a.log");
        a.ReportScroll(2);

        vm.MoveToNewGroup(a);

        Assert.True(a.IsScrollRestorePending);
        Assert.Equal(2, a.ScrollLine);
    }

    [Fact]
    public async Task With_every_tab_undocked_clicking_the_empty_main_window_keeps_the_active_tab()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");
        var group = vm.MoveToNewGroup(Tab(vm, "a.log"))!;
        vm.MoveTab(Tab(vm, "b.log"), group, 1);
        Assert.Empty(vm.MainGroup.Tabs);

        vm.ActivateGroup(vm.MainGroup); // e.g. the filter button on the main toolbar

        Assert.Same(group, vm.ActiveGroup);
        Assert.Same(Tab(vm, "b.log"), vm.SelectedTab); // the filter acts on it
        Assert.True(vm.CloseTabCommand.CanExecute(null));

        var path = Path.Combine(_dir, "c.log");
        File.WriteAllText(path, "1\n");
        await vm.OpenFileAsync(path); // opened from the main window: it opens there

        Assert.Equal(["c.log"], Names(vm.MainGroup));
        Assert.Same(vm.MainGroup, vm.ActiveGroup);
        Assert.Same(Tab(vm, "c.log"), vm.SelectedTab);
    }

    [Fact]
    public async Task Closing_the_active_floating_window_falls_back_to_a_window_with_tabs()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");
        var first = vm.MoveToNewGroup(Tab(vm, "a.log"))!;
        var second = vm.MoveToNewGroup(Tab(vm, "b.log"))!;
        Assert.Same(second, vm.ActiveGroup);

        vm.CloseTabCommand.Execute(Tab(vm, "b.log")); // the second window empties and closes

        Assert.Equal([first], vm.FloatingGroups);
        Assert.Same(first, vm.ActiveGroup);
        Assert.Same(Tab(vm, "a.log"), vm.SelectedTab);
    }

    [Fact]
    public async Task Closing_a_floating_window_with_the_main_window_empty_docks_its_tabs()
    {
        using var vm = await CreateWithTabs("a.log", "b.log");
        var group = vm.MoveToNewGroup(Tab(vm, "a.log"))!;
        vm.MoveTab(Tab(vm, "b.log"), group, 1);
        vm.ActivateGroup(vm.MainGroup);

        vm.CloseFloatingGroup(group);

        Assert.Equal(["a.log", "b.log"], Names(vm.MainGroup));
        Assert.Empty(vm.FloatingGroups);
        Assert.Same(vm.MainGroup, vm.ActiveGroup);
        Assert.Same(Tab(vm, "b.log"), vm.SelectedTab);
        Assert.True(Tab(vm, "b.log").IsSelected);
    }
}
