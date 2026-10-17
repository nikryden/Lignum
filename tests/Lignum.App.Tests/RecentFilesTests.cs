using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>The File > Open recent list (ticket 16), kept in settings.json.</summary>
public sealed class RecentFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly string _settingsDir;

    public RecentFilesTests()
    {
        Directory.CreateDirectory(_dir);
        _settingsDir = Path.Combine(_dir, "settings");
    }

    public void Dispose()
    {
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A fresh app start: settings are read from disk again.</summary>
    private MainViewModel Start() => UiContext.None(() =>
        new MainViewModel(new SettingsStore(_settingsDir, Logger.None).Open<AppSettings>(AppSettings.FileName)));

    private string WriteLog(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "line\n");
        return path;
    }

    [Fact]
    public async Task Opened_files_are_listed_most_recent_first_and_survive_a_restart()
    {
        var a = WriteLog("a.log");
        var b = WriteLog("b.log");
        using (var vm = Start())
        {
            await vm.OpenFileAsync(a);
            await vm.OpenFileAsync(b);
            Assert.Equal([b, a], vm.RecentFiles);
        }

        using var restarted = Start();
        Assert.Equal([b, a], restarted.RecentFiles);
        Assert.True(restarted.HasRecentFiles);
    }

    [Fact]
    public async Task Reopening_a_file_moves_it_to_the_top_without_duplicating_it()
    {
        using var vm = Start();
        var a = WriteLog("a.log");
        var b = WriteLog("b.log");
        await vm.OpenFilesAsync([a, b]);

        await vm.OpenFileAsync(a.ToUpperInvariant()); // same file, other spelling

        Assert.Equal([a, b], vm.RecentFiles);
    }

    [Fact]
    public async Task The_list_is_capped()
    {
        using var vm = Start();
        var paths = Enumerable.Range(0, MainViewModel.MaxRecentFiles + 3).Select(i => WriteLog($"f{i}.log")).ToList();

        await vm.OpenFilesAsync(paths);

        Assert.Equal(MainViewModel.MaxRecentFiles, vm.RecentFiles.Count);
        Assert.Equal(paths[^1], vm.RecentFiles[0]);
        Assert.DoesNotContain(paths[0], vm.RecentFiles);
    }

    [Fact]
    public async Task A_recent_file_that_no_longer_exists_is_skipped_and_removed_with_a_message()
    {
        var gone = WriteLog("gone.log");
        var kept = WriteLog("kept.log");
        using (var vm = Start())
        {
            await vm.OpenFilesAsync([gone, kept]);
        }
        File.Delete(gone);

        using var restarted = Start();
        await restarted.OpenRecentCommand.ExecuteAsync(gone);

        Assert.Empty(restarted.Tabs);
        Assert.NotNull(restarted.ErrorMessage);
        Assert.Contains("gone.log", restarted.ErrorMessage);
        Assert.Equal([kept], restarted.RecentFiles);
        using var again = Start();
        Assert.Equal([kept], again.RecentFiles);
    }

    [Fact]
    public async Task Opening_a_recent_file_opens_it_in_a_tab()
    {
        var a = WriteLog("a.log");
        using (var vm = Start()) await vm.OpenFileAsync(a);

        using var restarted = Start();
        await restarted.OpenRecentCommand.ExecuteAsync(a);

        Assert.Equal("a.log", restarted.SelectedTab!.FileName);
        Assert.Equal(1, restarted.SelectedTab.LineCount);
    }
}
