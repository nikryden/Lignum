using System.IO.Compression;
using System.Text.Json.Nodes;
using Lignum.App.Packaging;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>.lm packages (ticket 06): a log with its bookmarks and notes, exported and opened as one file.</summary>
public sealed class PackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public PackageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush(); // settings are written in the background: let them finish first
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string Extracted => Path.Combine(_dir, "extracted");

    private MainViewModel Start()
    {
        var vm = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName))
        {
            PackageFolder = Extracted,
        });
        _owned.Add(vm);
        return vm;
    }

    private string WriteLog(string name, int lines = 100)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, Enumerable.Range(0, lines).Select(i => $"line {i}"));
        return path;
    }

    private async Task<string> ExportWithNotes(bool compress)
    {
        var vm = Start();
        await vm.OpenFileAsync(WriteLog("app.log"));
        vm.SelectedTab!.Bookmarks.Toggle(4);
        vm.SelectedTab.Bookmarks.SetNote(12, "first error");
        var package = Path.Combine(_dir, compress ? "app.lm" : "app-stored.lm");
        await vm.ExportPackageAsync(package, compress);
        Assert.Null(vm.ErrorMessage);
        return package;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Export_then_open_gives_the_same_lines_bookmarks_and_notes(bool compress)
    {
        var package = await ExportWithNotes(compress);

        var vm = Start();
        await vm.OpenFileAsync(package);

        var tab = vm.SelectedTab!;
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(100, tab.LineCount);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_dir, "app.log")), File.ReadAllBytes(tab.Path));
        Assert.Equal([new BookmarkItem(4, null), new BookmarkItem(12, "first error")], tab.Bookmarks.Items);
        Assert.Equal(package, tab.SourcePath);
    }

    [Fact]
    public async Task Uncompressed_export_stores_the_log_as_is()
    {
        var package = await ExportWithNotes(compress: false);

        using var zip = ZipFile.OpenRead(package);
        var log = zip.GetEntry("app.log")!;
        Assert.Equal(log.Length, log.CompressedLength);
    }

    [Fact]
    public async Task A_log_that_does_not_match_the_manifest_opens_with_a_warning()
    {
        var package = await ExportWithNotes(compress: true);
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("manifest.json")!;
            JsonNode manifest;
            using (var read = entry.Open()) manifest = JsonNode.Parse(read)!;
            manifest["sha256"] = new string('0', 64);
            entry.Delete();
            using var write = zip.CreateEntry("manifest.json").Open();
            using var writer = new System.Text.Json.Utf8JsonWriter(write);
            manifest.WriteTo(writer);
        }

        var vm = Start();
        await vm.OpenFileAsync(package);

        Assert.NotNull(vm.SelectedTab);
        Assert.Contains("checksum", vm.ErrorMessage);
    }

    [Fact]
    public async Task Package_notes_stay_with_the_package_and_are_included_when_exported_again()
    {
        var package = await ExportWithNotes(compress: true);
        var vm = Start();
        await vm.OpenFileAsync(package);
        vm.SelectedTab!.Bookmarks.SetNote(30, "added by the recipient");

        await vm.OpenFileAsync(Path.Combine(_dir, "app.log")); // the same log, opened directly
        Assert.DoesNotContain(vm.SelectedTab!.Bookmarks.Items, b => b.Line == 30);

        vm.SelectedTab = vm.Tabs[0];
        var again = Path.Combine(_dir, "again.lm");
        await vm.ExportPackageAsync(again, compress: true);
        await Start().OpenFileAsync(again);
        var reopened = _owned.OfType<MainViewModel>().Last().SelectedTab!;
        Assert.Equal("added by the recipient", reopened.Bookmarks.NoteFor(30));
        Assert.Equal("first error", reopened.Bookmarks.NoteFor(12));
    }

    [Fact]
    public async Task Closing_a_package_tab_deletes_its_extracted_copy()
    {
        var package = await ExportWithNotes(compress: true);
        var vm = Start();
        await vm.OpenFileAsync(package);
        var folder = vm.SelectedTab!.Package!.Folder;
        Assert.True(Directory.Exists(folder));

        vm.CloseTabCommand.Execute(null);

        for (var i = 0; i < 50 && Directory.Exists(folder); i++) await Task.Delay(100);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task A_cancelled_export_leaves_no_file()
    {
        var destination = Path.Combine(_dir, "cancelled.lm");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LogPackage.ExportAsync(
            WriteLog("big.log", 50_000), new BookmarksFile(), null, destination, true, null, cancel.Token));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".tmp"));
    }

    [Fact]
    public async Task A_file_that_is_not_a_package_gives_an_error_and_no_tab()
    {
        var bad = Path.Combine(_dir, "bad.lm");
        File.WriteAllText(bad, "not a zip");

        var vm = Start();
        await vm.OpenFileAsync(bad);

        Assert.Empty(vm.Tabs);
        Assert.Contains("bad.lm", vm.ErrorMessage);
    }
}
