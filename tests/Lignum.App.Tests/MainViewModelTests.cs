using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public MainViewModelTests() => Directory.CreateDirectory(_dir);

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
    public void Before_opening_a_file_the_title_is_just_the_app_name()
    {
        using var vm = Create();

        Assert.Equal("Lignum", vm.Title);
        Assert.Empty(vm.Tabs);
        Assert.Null(vm.SelectedTab);
    }

    [Fact]
    public async Task Opening_a_file_shows_its_name_and_line_count()
    {
        using var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.OpenFileAsync(WriteLog("service.log", "a\nb\nc\n"));

        var tab = vm.SelectedTab!;
        Assert.Equal("service.log", tab.FileName);
        Assert.Equal("service.log - Lignum", vm.Title);
        Assert.Equal(3, tab.LineCount);
        Assert.False(tab.IsIndexing);
        Assert.Contains(nameof(MainViewModel.Title), changed);
    }

    [Fact]
    public async Task Line_count_changes_are_reported()
    {
        using var vm = Create();
        LogTabViewModel? tab = null;
        var changed = new List<string?>();
        vm.Tabs.CollectionChanged += (_, e) =>
        {
            tab = (LogTabViewModel)e.NewItems![0]!;
            tab.PropertyChanged += (_, p) => changed.Add(p.PropertyName);
        };

        await vm.OpenFileAsync(WriteLog("service.log", "a\nb\nc\n"));

        Assert.Contains(nameof(LogTabViewModel.LineCount), changed);
    }

    [Fact]
    public async Task Opening_a_file_gives_the_viewer_a_source_for_its_lines()
    {
        using var vm = Create();

        await vm.OpenFileAsync(WriteLog("app.log", "alpha\nbeta\n"));

        var lines = vm.SelectedTab!.Lines;
        Assert.NotNull(lines);
        Assert.Equal(2, lines.RowCount);
        Assert.Equal(["alpha", "beta"], (await lines.ReadRowsAsync(0, 10)).Select(r => r.Text));

        await vm.OpenFileAsync(WriteLog("other.log", "gamma\n"));
        Assert.NotSame(lines, vm.SelectedTab!.Lines);
        Assert.Equal("gamma", (await vm.SelectedTab.Lines!.ReadRowsAsync(0, 1))[0].Text);
    }

    [Fact]
    public async Task Opening_a_missing_file_reports_an_error_and_keeps_the_previous_state()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteLog("ok.log", "a\n"));

        await vm.OpenFileAsync(Path.Combine(_dir, "nope.log"));

        Assert.Single(vm.Tabs);
        Assert.Equal("ok.log", vm.SelectedTab!.FileName);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("nope.log", vm.ErrorMessage);
    }

    [Fact]
    public async Task Cancel_command_stops_indexing()
    {
        using var vm = Create();
        var path = Path.Combine(_dir, "big.log");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat(new string('z', 99) + "\n", 100_000)));
        vm.Tabs.CollectionChanged += (_, e) =>
        {
            var tab = (LogTabViewModel)e.NewItems![0]!;
            tab.PropertyChanged += (_, p) =>
            {
                if (p.PropertyName == nameof(LogTabViewModel.LineCount) && tab.CancelIndexingCommand.CanExecute(null))
                    tab.CancelIndexingCommand.Execute(null);
            };
        };

        await vm.OpenFileAsync(path);

        var tab = vm.SelectedTab!;
        Assert.False(tab.IsIndexing);
        Assert.InRange(tab.LineCount, 1, 99_999);
        Assert.Contains("cancelled", tab.StatusText, StringComparison.OrdinalIgnoreCase);
    }
}
