using Lignum.App.Settings;
using Lignum.App.Tests.Support;
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

    private static DetailPaneViewModel Create(params string[] lines) =>
        UiContext.None(() => new DetailPaneViewModel { Reader = new InMemoryReader(lines) });

    private DetailPaneLayoutViewModel CreateLayout() => UiContext.None(() => new DetailPaneLayoutViewModel(OpenSettings()));

    // ---- content (per tab) ----

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
        Assert.NotEqual("", vm.Notice);
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

    // ---- layout (app-wide) ----

    [Fact]
    public void By_default_the_pane_is_open_and_docked_right()
    {
        var layout = CreateLayout();

        Assert.True(layout.IsOpen);
        Assert.Equal(DetailPaneDock.Right, layout.Dock);
    }

    [Fact]
    public void Collapse_and_dock_position_are_remembered_across_restarts()
    {
        var layout = CreateLayout();
        layout.ToggleCommand.Execute(null);
        layout.DockBottomCommand.Execute(null);
        layout.PaneHeight = 250;

        var restarted = CreateLayout();

        Assert.False(restarted.IsOpen);
        Assert.Equal(DetailPaneDock.Bottom, restarted.Dock);
        Assert.Equal(250, restarted.PaneHeight);
    }

    [Fact]
    public void Docking_right_again_keeps_the_remembered_width()
    {
        var layout = CreateLayout();
        layout.PaneWidth = 333;
        layout.DockBottomCommand.Execute(null);
        layout.DockRightCommand.Execute(null);

        Assert.Equal(DetailPaneDock.Right, layout.Dock);
        Assert.Equal(333, layout.PaneWidth);
    }

    [Theory]
    [InlineData(-5, DetailPaneLayoutViewModel.MinPaneSize)]
    [InlineData(double.NaN, 400)]
    [InlineData(500, 500)]
    public void Pane_sizes_are_kept_sensible(double requested, double expected)
    {
        var layout = CreateLayout();
        layout.PaneWidth = requested;

        Assert.Equal(expected, layout.PaneWidth);
    }

    [Fact]
    public void Without_a_settings_file_the_layout_still_works()
    {
        var layout = new DetailPaneLayoutViewModel();
        layout.ToggleCommand.Execute(null);

        Assert.False(layout.IsOpen);
    }

    private sealed class InMemoryReader(string[] lines) : ILineReader
    {
        public string[] ReadLines(long firstLine, int count) =>
            lines.Skip((int)firstLine).Take(count).ToArray();
    }
}
