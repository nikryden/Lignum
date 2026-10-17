using Lignum.Core;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.App.Viewer;
using Serilog.Core;

namespace Lignum.App.Tests;

public class StatusBarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public StatusBarTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
    public async Task Opening_a_file_shows_its_size()
    {
        using var vm = Create();

        await vm.OpenFileAsync(WriteLog("a.log", new string('x', 2047) + "\n"));

        Assert.Equal(2048, vm.SelectedTab!.FileSize);
        Assert.Equal(ByteSize.Format(2048), vm.SelectedTab.FileSizeText);
    }

    [Fact]
    public async Task Go_to_line_works_against_the_lines_indexed()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteLog("a.log", "1\n2\n3\n"));

        Assert.True(vm.SelectedTab!.Navigation.TryGoToLine("3", out _));
        Assert.Equal(new FilePosition(2, 0), vm.SelectedTab!.Navigation.Caret);
        Assert.False(vm.SelectedTab!.Navigation.TryGoToLine("4", out var error));
        Assert.Equal("Enter a line between 1 and 3.", error);
    }

    [Fact]
    public async Task Each_tab_keeps_its_own_caret()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteLog("a.log", "1\n2\n3\n"));
        var first = vm.SelectedTab!;
        first.Navigation.GoToEndCommand.Execute(null);

        await vm.OpenFileAsync(WriteLog("b.log", "x\n"));

        Assert.Equal(FilePosition.Start, vm.SelectedTab!.Navigation.Caret);
        Assert.Equal("Ln 1, Col 1", vm.SelectedTab.Navigation.CaretText);
        Assert.Equal(new FilePosition(2, 0), first.Navigation.Caret);
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
