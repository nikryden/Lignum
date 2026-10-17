using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

public sealed class MainViewModelDetailPaneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public MainViewModelDetailPaneTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private MainViewModel Create() => UiContext.None(() => new MainViewModel(
        new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));

    private string WriteFile(string content)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Show_full_line_opens_a_collapsed_pane_on_that_line_of_the_selected_tab()
    {
        var huge = new string('j', 50_000);
        using var vm = Create();
        await vm.OpenFileAsync(WriteFile($"a\n{huge}\n")).WaitAsync(TimeSpan.FromSeconds(30));
        vm.DetailPane.ToggleCommand.Execute(null);
        Assert.False(vm.DetailPane.IsOpen);

        vm.ShowFullLineCommand.Execute(1L);
        await vm.SelectedTab!.Detail.WhenLoaded;

        Assert.True(vm.DetailPane.IsOpen);
        Assert.Equal(1, vm.SelectedTab.Detail.CurrentLine);
        Assert.Equal(huge, vm.SelectedTab.Detail.Text);
    }

    [Fact]
    public async Task The_detail_pane_follows_the_caret_line()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteFile("zero\none\ntwo\n")).WaitAsync(TimeSpan.FromSeconds(30));
        var tab = vm.SelectedTab!;

        Assert.True(tab.Navigation.TryGoToLine("3", out _));
        await tab.Detail.WhenLoaded;

        Assert.Equal(2, tab.Detail.CurrentLine);
        Assert.Equal("two", tab.Detail.Text);
    }

    [Fact]
    public async Task Selecting_several_lines_shows_them_all_in_the_detail_pane()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteFile("zero\none\ntwo\nthree\n")).WaitAsync(TimeSpan.FromSeconds(30));
        var tab = vm.SelectedTab!;

        tab.Selection.MoveCaret(new Lignum.Core.FilePosition(1, 0), extend: false);
        tab.Selection.MoveCaret(new Lignum.Core.FilePosition(2, 2), extend: true);
        await tab.Detail.WhenLoaded;
        Assert.Equal("one\ntwo", tab.Detail.Text);

        tab.Selection.MoveCaret(new Lignum.Core.FilePosition(3, 1), extend: false);
        await tab.Detail.WhenLoaded;
        Assert.Equal("three", tab.Detail.Text);
    }

    [Fact]
    public async Task The_hidden_lines_setting_reaches_every_tab()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteFile("one\n")).WaitAsync(TimeSpan.FromSeconds(30));

        vm.DetailPane.IncludeHiddenLines = true;
        await vm.OpenFileAsync(WriteFile("two\n")).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(vm.Tabs, tab => Assert.True(tab.Detail.IncludeHiddenLines));
    }

    [Fact]
    public async Task Each_tab_keeps_its_own_detail_line()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteFile("one\n")).WaitAsync(TimeSpan.FromSeconds(30));
        var first = vm.SelectedTab!;
        first.Detail.CurrentLine = 0;
        await first.Detail.WhenLoaded;

        await vm.OpenFileAsync(WriteFile("two\n")).WaitAsync(TimeSpan.FromSeconds(30));
        await vm.SelectedTab!.Detail.WhenLoaded;

        Assert.NotSame(first, vm.SelectedTab);
        Assert.Null(vm.SelectedTab.Detail.CurrentLine);
        Assert.Equal("one", first.Detail.Text);
    }
}
