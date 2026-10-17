using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>The "File changed: Reload" banner and the reload it offers.</summary>
public class FileChangeBannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public FileChangeBannerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private MainViewModel Create() => UiContext.None(() =>
        new MainViewModel(new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));

    private string WriteLog(string content)
    {
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Change notifications arrive on a background thread; give them time to land.</summary>
    private static void WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the view model.");
            Thread.Sleep(20);
        }
    }

    [Fact]
    public async Task No_banner_while_the_file_is_unchanged()
    {
        using var main = Create();
        await main.OpenFileAsync(WriteLog("a\n"));
        var vm = main.SelectedTab!;

        Assert.False(vm.IsFileChanged);
        Assert.False(vm.ReloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Growth_shows_the_banner_and_keeps_the_lines()
    {
        using var main = Create();
        var path = WriteLog("a\nb\n");
        await main.OpenFileAsync(path);
        var vm = main.SelectedTab!;

        File.AppendAllText(path, "c\n");

        WaitUntil(() => vm.IsFileChanged);
        Assert.Equal(2, vm.LineCount);
        Assert.True(vm.ReloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Truncation_shows_the_banner_and_drops_the_stale_lines()
    {
        using var main = Create();
        var path = WriteLog("a\nb\nc\n");
        await main.OpenFileAsync(path);
        var vm = main.SelectedTab!;

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            stream.SetLength(2);

        WaitUntil(() => vm.IsFileChanged);
        Assert.Equal(0, vm.LineCount);
        Assert.Contains("truncated", vm.FileChangeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reload_rebuilds_the_index_and_hides_the_banner()
    {
        using var main = Create();
        var path = WriteLog("a\nb\nc\n");
        await main.OpenFileAsync(path);
        var vm = main.SelectedTab!;
        File.WriteAllText(path, "x\n"); // shrinks it
        WaitUntil(() => vm.IsFileChanged);

        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.False(vm.IsFileChanged);
        Assert.Equal(1, vm.LineCount);
        Assert.Equal("app.log", vm.FileName);
        Assert.Single(main.Tabs); // reloaded in place
        Assert.Same(vm, main.SelectedTab);
    }

    [Fact]
    public async Task Reload_after_rotation_opens_the_new_file()
    {
        using var main = Create();
        var path = WriteLog("old\n");
        await main.OpenFileAsync(path);
        var vm = main.SelectedTab!;
        File.Move(path, path + ".1");
        File.WriteAllText(path, "new 1\nnew 2\n");
        WaitUntil(() => vm.IsFileChanged);

        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.False(vm.IsFileChanged);
        Assert.Equal(2, vm.LineCount);
    }
}
