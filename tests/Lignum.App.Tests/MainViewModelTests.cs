using System.ComponentModel;
using Lignum.App.ViewModels;

namespace Lignum.App.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public MainViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteLog(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Before_opening_a_file_the_title_is_just_the_app_name()
    {
        using var vm = new MainViewModel();

        Assert.Equal("Lignum", vm.Title);
        Assert.Null(vm.FileName);
        Assert.Equal(0, vm.LineCount);
        Assert.False(vm.IsIndexing);
    }

    [Fact]
    public async Task Opening_a_file_shows_its_name_and_line_count()
    {
        using var vm = new MainViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.OpenFileAsync(WriteLog("service.log", "a\nb\nc\n"));

        Assert.Equal("service.log", vm.FileName);
        Assert.Equal("service.log - Lignum", vm.Title);
        Assert.Equal(3, vm.LineCount);
        Assert.False(vm.IsIndexing);
        Assert.Contains(nameof(MainViewModel.LineCount), changed);
        Assert.Contains(nameof(MainViewModel.Title), changed);
    }

    [Fact]
    public async Task Opening_another_file_replaces_the_first()
    {
        using var vm = new MainViewModel();
        var first = WriteLog("first.log", "1\n2\n");
        await vm.OpenFileAsync(first);

        await vm.OpenFileAsync(WriteLog("second.log", "only\n"));

        Assert.Equal("second.log", vm.FileName);
        Assert.Equal(1, vm.LineCount);
        File.Delete(first); // the first document no longer holds anything that matters
    }

    [Fact]
    public async Task Opening_a_file_gives_the_viewer_a_source_for_its_lines()
    {
        using var vm = new MainViewModel();
        Assert.Null(vm.Lines);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.OpenFileAsync(WriteLog("app.log", "alpha\nbeta\n"));

        Assert.NotNull(vm.Lines);
        Assert.Contains(nameof(MainViewModel.Lines), changed);
        Assert.Equal(2, vm.Lines.RowCount);
        Assert.Equal(["alpha", "beta"], (await vm.Lines.ReadRowsAsync(0, 10)).Select(r => r.Text));

        var first = vm.Lines;
        await vm.OpenFileAsync(WriteLog("other.log", "gamma\n"));
        Assert.NotSame(first, vm.Lines);
        Assert.Equal("gamma", (await vm.Lines!.ReadRowsAsync(0, 1))[0].Text);
    }

    [Fact]
    public async Task Opening_a_missing_file_reports_an_error_and_keeps_the_previous_state()
    {
        using var vm = new MainViewModel();

        await vm.OpenFileAsync(Path.Combine(_dir, "nope.log"));

        Assert.Null(vm.FileName);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("nope.log", vm.ErrorMessage);
    }

    [Fact]
    public async Task Cancel_command_stops_indexing()
    {
        using var vm = new MainViewModel();
        var path = Path.Combine(_dir, "big.log");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat(new string('z', 99) + "\n", 100_000)));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.LineCount) && vm.CancelIndexingCommand.CanExecute(null))
                vm.CancelIndexingCommand.Execute(null);
        };

        await vm.OpenFileAsync(path);

        Assert.False(vm.IsIndexing);
        Assert.InRange(vm.LineCount, 1, 99_999);
        Assert.Contains("cancelled", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }
}
