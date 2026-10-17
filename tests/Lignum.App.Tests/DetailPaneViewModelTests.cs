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
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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

    // ---- several selected lines ----

    [Fact]
    public async Task A_range_shows_every_line_and_copies_them_all()
    {
        var vm = Create("a", "b", "c", "d");

        vm.ShowRange(1, 3);
        await vm.WhenLoaded;

        Assert.Equal("b\nc\nd", vm.Text);
        Assert.Equal("Lines 2–4 (3 lines)", vm.Header);
        Assert.True(vm.IsMultiLine);
        Assert.Equal("Copy lines", vm.CopyLabel);
        Assert.Equal(string.Join(Environment.NewLine, "b", "c", "d"), await vm.ReadFullLineAsync());
    }

    [Fact]
    public async Task A_range_shows_only_the_shown_rows_unless_hidden_lines_are_included()
    {
        var vm = Create("a", "b", "c", "d", "e");
        vm.ShownLines = new Rows(0, 2, 4);

        vm.ShowRange(0, 4);
        await vm.WhenLoaded;
        Assert.Equal("a\nc\ne", vm.Text);
        Assert.Equal("Lines 1–5 (3 lines)", vm.Header);

        vm.IncludeHiddenLines = true;
        await vm.WhenLoaded;
        Assert.Equal("a\nb\nc\nd\ne", vm.Text);
    }

    [Fact]
    public async Task A_huge_range_is_capped_with_a_notice()
    {
        var vm = Create([.. Enumerable.Range(0, DetailPaneViewModel.MaxShownLines + 5).Select(i => $"l{i}")]);

        vm.ShowRange(0, DetailPaneViewModel.MaxShownLines + 4);
        await vm.WhenLoaded;

        Assert.Equal(DetailPaneViewModel.MaxShownLines, vm.Text.Split('\n').Length);
        Assert.True(vm.IsCapped);
        Assert.Contains("Ctrl+C", vm.Notice);
    }

    [Fact]
    public async Task Setting_the_current_line_goes_back_to_one_line()
    {
        var vm = Create("a", "b", "c");
        vm.ShowRange(0, 2);
        await vm.WhenLoaded;

        vm.CurrentLine = 1;
        await vm.WhenLoaded;

        Assert.Equal("b", vm.Text);
        Assert.Equal("Line 2", vm.Header);
        Assert.False(vm.IsMultiLine);
    }

    [Fact]
    public void Including_hidden_lines_is_off_by_default_and_remembered()
    {
        Assert.False(CreateLayout().IncludeHiddenLines);
        CreateLayout().IncludeHiddenLines = true;
        SettingsStore.Flush();

        Assert.True(CreateLayout().IncludeHiddenLines);
    }

    private sealed class Rows(params long[] lines) : ILineList
    {
        public long Count => lines.Length;

        public long this[long row] => lines[row];

        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class InMemoryReader(string[] lines) : ILineReader
    {
        public string[] ReadLines(long firstLine, int count) =>
            lines.Skip((int)firstLine).Take(count).ToArray();
    }
}
