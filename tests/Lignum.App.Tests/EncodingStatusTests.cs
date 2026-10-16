using System.Text;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Lignum.Core;
using Serilog.Core;

namespace Lignum.App.Tests;

public class EncodingStatusTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public EncodingStatusTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private MainViewModel Create() => UiContext.None(() =>
        new MainViewModel(new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));

    private string WriteLog(string content, Encoding encoding)
    {
        var path = Path.Combine(_dir, "a.log");
        File.WriteAllText(path, content, encoding);
        return path;
    }

    private static async Task<string[]> ReadAll(LogTabViewModel tab)
    {
        var rows = await tab.Lines!.ReadRowsAsync(0, 100, CancellationToken.None);
        return rows.Select(r => r.Text).ToArray();
    }

    [Fact]
    public async Task The_status_bar_shows_the_detected_encoding()
    {
        using var vm = Create();

        await vm.OpenFileAsync(WriteLog("one\ntwo\n", new UnicodeEncoding(false, true)));

        Assert.Equal("UTF-16 LE", vm.SelectedTab!.EncodingText);
        Assert.Equal(LogEncoding.Utf16LE, vm.SelectedTab.Encoding);
        Assert.Equal(["one", "two"], await ReadAll(vm.SelectedTab));
    }

    [Fact]
    public async Task Choosing_another_encoding_reindexes_the_tab_with_it()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteLog("café\n", new UTF8Encoding(false)));
        var tab = vm.SelectedTab!;
        Assert.Equal("UTF-8", tab.EncodingText);

        await tab.ChangeEncodingCommand.ExecuteAsync(LogEncoding.Windows1252);

        Assert.Equal("Windows-1252", tab.EncodingText);
        Assert.True(tab.IsEncodingOverridden);
        Assert.Equal(["cafÃ©"], await ReadAll(tab));
        Assert.Same(tab, vm.SelectedTab); // same tab, not a new one
    }

    [Fact]
    public async Task Choosing_automatic_detection_drops_the_override()
    {
        using var vm = Create();
        await vm.OpenFileAsync(WriteLog("café\n", new UTF8Encoding(false)));
        var tab = vm.SelectedTab!;
        await tab.ChangeEncodingCommand.ExecuteAsync(LogEncoding.Windows1252);

        await tab.ChangeEncodingCommand.ExecuteAsync(null);

        Assert.Equal("UTF-8", tab.EncodingText);
        Assert.False(tab.IsEncodingOverridden);
        Assert.Equal(["café"], await ReadAll(tab));
    }
}
