using Lignum.App.ViewModels;

namespace Lignum.App.Tests;

public sealed class MainViewModelDetailPaneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public MainViewModelDetailPaneTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteFile(string content)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task The_detail_pane_shows_full_lines_of_the_open_file()
    {
        var huge = new string('j', 50_000);
        using var vm = new MainViewModel();
        await vm.OpenFileAsync(WriteFile($"a\n{huge}\n")).WaitAsync(TimeSpan.FromSeconds(30));

        vm.Detail.ShowFullLine(1);
        await vm.Detail.WhenLoaded;

        Assert.Equal(huge, vm.Detail.Text);
    }

    [Fact]
    public async Task Opening_another_file_clears_the_detail_pane()
    {
        using var vm = new MainViewModel();
        await vm.OpenFileAsync(WriteFile("one\n")).WaitAsync(TimeSpan.FromSeconds(30));
        vm.Detail.CurrentLine = 0;
        await vm.Detail.WhenLoaded;

        await vm.OpenFileAsync(WriteFile("two\n")).WaitAsync(TimeSpan.FromSeconds(30));
        await vm.Detail.WhenLoaded;

        Assert.Null(vm.Detail.CurrentLine);
        Assert.Equal("", vm.Detail.Text);
    }

    [Fact]
    public void The_window_level_detail_pane_can_be_passed_in()
    {
        var detail = new DetailPaneViewModel();

        using var vm = new MainViewModel(detail);

        Assert.Same(detail, vm.Detail);
    }
}
