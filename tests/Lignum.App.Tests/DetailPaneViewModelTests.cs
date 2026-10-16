using Lignum.App.Settings;
using Lignum.App.Viewer;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

public sealed class DetailPaneViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SettingsFile<AppSettings> OpenSettings() =>
        new SettingsStore(_dir, Logger.None).Open<AppSettings>(AppSettings.FileName);

    private DetailPaneViewModel Create(params string[] lines) =>
        new(OpenSettings()) { Reader = new InMemoryReader(lines) };

    // ---- content ----

    [Fact]
    public async Task Shows_the_current_line_in_full_with_its_one_based_number()
    {
        var huge = new string('x', 200_000);
        var vm = Create("first", huge);

        vm.CurrentLine = 1;
        await vm.WhenLoaded;

        Assert.Equal(huge, vm.Text);
        Assert.Equal("Line 2", vm.Header);
        Assert.False(vm.IsCapped);
    }

    [Fact]
    public async Task No_current_line_shows_nothing()
    {
        var vm = Create("first");
        vm.CurrentLine = 0;
        await vm.WhenLoaded;

        vm.CurrentLine = null;
        await vm.WhenLoaded;

        Assert.Equal("", vm.Text);
        Assert.Equal("No line selected", vm.Header);
    }

    [Fact]
    public async Task A_line_too_big_for_the_text_box_is_capped_with_a_notice_but_copies_in_full()
    {
        var giant = new string('z', DetailPaneViewModel.MaxShownChars + 5);
        var vm = Create(giant);

        vm.CurrentLine = 0;
        await vm.WhenLoaded;

        Assert.True(vm.IsCapped);
        Assert.Equal(DetailPaneViewModel.MaxShownChars, vm.Text.Length);
        Assert.Equal(giant, await vm.ReadFullLineAsync());
    }

    [Fact]
    public async Task A_new_reader_clears_the_pane()
    {
        var vm = Create("first");
        vm.CurrentLine = 0;
        await vm.WhenLoaded;

        vm.Reader = new InMemoryReader(["other"]);
        await vm.WhenLoaded;

        Assert.Null(vm.CurrentLine);
        Assert.Equal("", vm.Text);
    }

    [Fact]
    public async Task Show_full_line_opens_a_collapsed_pane_on_that_line()
    {
        var vm = Create("a", "b", "c");
        vm.ToggleCommand.Execute(null);
        Assert.False(vm.IsOpen);

        vm.ShowFullLine(2);
        await vm.WhenLoaded;

        Assert.True(vm.IsOpen);
        Assert.Equal(2, vm.CurrentLine);
        Assert.Equal("c", vm.Text);
    }

    // ---- layout ----

    [Fact]
    public void By_default_the_pane_is_open_and_docked_right()
    {
        var vm = Create();

        Assert.True(vm.IsOpen);
        Assert.Equal(DetailPaneDock.Right, vm.Dock);
    }

    [Fact]
    public void Collapse_and_dock_position_are_remembered_across_restarts()
    {
        var vm = new DetailPaneViewModel(OpenSettings());
        vm.ToggleCommand.Execute(null);
        vm.DockBottomCommand.Execute(null);
        vm.PaneHeight = 250;

        var restarted = new DetailPaneViewModel(OpenSettings());

        Assert.False(restarted.IsOpen);
        Assert.Equal(DetailPaneDock.Bottom, restarted.Dock);
        Assert.Equal(250, restarted.PaneHeight);
    }

    [Fact]
    public void Docking_right_again_keeps_the_remembered_width()
    {
        var vm = Create();
        vm.PaneWidth = 333;
        vm.DockBottomCommand.Execute(null);
        vm.DockRightCommand.Execute(null);

        Assert.Equal(DetailPaneDock.Right, vm.Dock);
        Assert.Equal(333, vm.PaneWidth);
    }

    [Theory]
    [InlineData(-5, DetailPaneViewModel.MinPaneSize)]
    [InlineData(double.NaN, 400)]
    [InlineData(500, 500)]
    public void Pane_sizes_are_kept_sensible(double requested, double expected)
    {
        var vm = Create();
        vm.PaneWidth = requested;

        Assert.Equal(expected, vm.PaneWidth);
    }

    [Fact]
    public void Without_a_settings_file_the_pane_still_works()
    {
        var vm = new DetailPaneViewModel();
        vm.ToggleCommand.Execute(null);

        Assert.False(vm.IsOpen);
    }

    private sealed class InMemoryReader(string[] lines) : ILineReader
    {
        public string[] ReadLines(long firstLine, int count) =>
            lines.Skip((int)firstLine).Take(count).ToArray();
    }
}
