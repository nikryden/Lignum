using Lignum.App.ViewModels;
using Lignum.App.Viewer;

namespace Lignum.App.Tests;

public class StatusBarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public StatusBarTests() => Directory.CreateDirectory(_dir);

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
    public async Task Opening_a_file_shows_its_size()
    {
        using var vm = new MainViewModel();
        Assert.Equal("", vm.FileSizeText);

        await vm.OpenFileAsync(WriteLog("a.log", new string('x', 2047) + "\n"));

        Assert.Equal(2048, vm.FileSize);
        Assert.Equal(ByteSize.Format(2048), vm.FileSizeText);
    }

    [Fact]
    public async Task Go_to_line_works_against_the_lines_indexed()
    {
        using var vm = new MainViewModel();
        await vm.OpenFileAsync(WriteLog("a.log", "1\n2\n3\n"));

        Assert.True(vm.Navigation.TryGoToLine("3", out _));
        Assert.Equal(new FilePosition(2, 0), vm.Navigation.Caret);
        Assert.False(vm.Navigation.TryGoToLine("4", out var error));
        Assert.Equal("Enter a line between 1 and 3.", error);
    }

    [Fact]
    public async Task Opening_another_file_puts_the_caret_back_at_the_start()
    {
        using var vm = new MainViewModel();
        await vm.OpenFileAsync(WriteLog("a.log", "1\n2\n3\n"));
        vm.Navigation.GoToEndCommand.Execute(null);

        await vm.OpenFileAsync(WriteLog("b.log", "x\n"));

        Assert.Equal(FilePosition.Start, vm.Navigation.Caret);
        Assert.Equal("Ln 1, Col 1", vm.Navigation.CaretText);
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(1023, "1,023 bytes")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10L * 1024 * 1024, "10.0 MB")]
    [InlineData(2_147_483_648L, "2.00 GB")]
    [InlineData(5L * 1024 * 1024 * 1024 * 1024, "5.00 TB")]
    public void File_sizes_read_naturally(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes, System.Globalization.CultureInfo.InvariantCulture));
}
