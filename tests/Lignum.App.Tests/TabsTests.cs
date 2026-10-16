using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Tabs, the open-or-switch rule and closing tabs (ticket 16).</summary>
public sealed class TabsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public TabsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private MainViewModel Create() => UiContext.None(() =>
        new MainViewModel(new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));

    private string WriteLog(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Opening_a_second_file_adds_a_tab_and_selects_it()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteLog("a.log", "1\n"));

        await vm.OpenFileAsync(WriteLog("b.log", "1\n2\n"));

        Assert.Equal(["a.log", "b.log"], vm.Tabs.Select(t => t.FileName));
        Assert.Same(vm.Tabs[1], vm.SelectedTab);
        Assert.Equal(2, vm.SelectedTab!.LineCount);
        Assert.Equal("b.log - Lignum", vm.Title);
    }

    [Fact]
    public async Task Opening_an_already_open_path_switches_to_its_tab()
    {
        using var vm = Create();
        var a = WriteLog("a.log", "1\n");
        await vm.OpenFileAsync(a);
        await vm.OpenFileAsync(WriteLog("b.log", "1\n"));
        var tabA = vm.Tabs[0];

        // Different case and a relative-looking segment: still the same file.
        await vm.OpenFileAsync(Path.Combine(_dir, "sub", "..", "A.LOG"));

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(tabA, vm.SelectedTab);
        Assert.Equal("a.log - Lignum", vm.Title);
    }

    [Fact]
    public async Task Opening_several_files_at_once_opens_each_in_its_own_tab()
    {
        using var vm = Create();
        var a = WriteLog("a.log", "1\n");
        var b = WriteLog("b.log", "1\n2\n");

        await vm.OpenFilesAsync([a, b, a]); // a dropped twice: still one tab

        Assert.Equal(["a.log", "b.log"], vm.Tabs.Select(t => t.FileName));
        Assert.All(vm.Tabs, t => Assert.False(t.IsIndexing));
        Assert.Equal(2, vm.Tabs[1].LineCount);
    }

    [Fact]
    public async Task Closing_the_current_tab_releases_its_file_and_selects_a_neighbour()
    {
        using var vm = Create();
        var a = WriteLog("a.log", "1\n");
        var b = WriteLog("b.log", "1\n");
        var c = WriteLog("c.log", "1\n");
        await vm.OpenFilesAsync([a, b, c]);
        vm.SelectedTab = vm.Tabs[1];

        vm.CloseTabCommand.Execute(null); // Ctrl+W: no parameter = the current tab

        Assert.Equal(["a.log", "c.log"], vm.Tabs.Select(t => t.FileName));
        Assert.Equal("c.log", vm.SelectedTab!.FileName);
        WaitUntil(() => CanOpenExclusively(b));
    }

    [Fact]
    public async Task Closing_another_tab_keeps_the_selection()
    {
        using var vm = Create();
        await vm.OpenFilesAsync([WriteLog("a.log", "1\n"), WriteLog("b.log", "1\n")]);

        vm.CloseTabCommand.Execute(vm.Tabs[0]);

        Assert.Equal("b.log", Assert.Single(vm.Tabs).FileName);
        Assert.Same(vm.Tabs[0], vm.SelectedTab);
    }

    [Fact]
    public async Task Closing_the_last_tab_leaves_an_empty_window()
    {
        using var vm = Create();
        Assert.False(vm.CloseTabCommand.CanExecute(null));
        await vm.OpenFileAsync(WriteLog("a.log", "1\n"));
        Assert.True(vm.CloseTabCommand.CanExecute(null));

        vm.CloseTabCommand.Execute(null);

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.SelectedTab);
        Assert.Equal("Lignum", vm.Title);
        Assert.False(vm.CloseTabCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_closed_file_can_be_opened_again_in_a_new_tab()
    {
        using var vm = Create();
        var a = WriteLog("a.log", "1\n");
        await vm.OpenFileAsync(a);
        var first = vm.SelectedTab;
        vm.CloseTabCommand.Execute(null);

        await vm.OpenFileAsync(a);

        Assert.NotSame(first, Assert.Single(vm.Tabs));
        Assert.Equal(1, vm.SelectedTab!.LineCount);
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting.");
            Thread.Sleep(20);
        }
    }

    [Fact]
    public async Task Each_tab_keeps_its_own_lines_and_state()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteLog("a.log", "alpha\n"));
        var tabA = vm.SelectedTab!;
        await vm.OpenFileAsync(WriteLog("b.log", "beta\nbeta2\n"));
        var tabB = vm.SelectedTab!;

        Assert.Equal("alpha", (await tabA.Lines!.ReadRowsAsync(0, 10)).Single().Text);
        Assert.Equal(2, tabB.Lines!.RowCount);
        Assert.False(tabA.IsSelected);
        Assert.True(tabB.IsSelected);

        vm.SelectedTab = tabA;
        Assert.True(tabA.IsSelected);
        Assert.False(tabB.IsSelected);
        Assert.Equal("a.log - Lignum", vm.Title);
    }
}
