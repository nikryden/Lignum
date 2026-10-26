using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Links in bookmark notes (ticket 23): web addresses and file or folder paths.</summary>
public sealed class NoteLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _owned = [];

    public NoteLinkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        SettingsStore.Flush();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string[] Targets(string note) => [.. NoteLinks.Find(note).Select(l => l.Target)];

    [Theory]
    [InlineData("see https://example.com/a?b=1 now", "https://example.com/a?b=1")]
    [InlineData("ticket http://jira/X-1.", "http://jira/X-1")]
    [InlineData("(HTTPS://Example.com)", "HTTPS://Example.com")]
    public void Finds_web_addresses_without_trailing_punctuation(string note, string expected) =>
        Assert.Equal([expected], Targets(note));

    [Fact]
    public void Finds_several_links_in_order_with_their_positions()
    {
        const string note = "https://a.example and http://b.example";

        var links = NoteLinks.Find(note);

        Assert.Equal(["https://a.example", "http://b.example"], links.Select(l => l.Target));
        Assert.Equal("http://b.example", note.Substring(links[1].Start, links[1].Length));
    }

    [Theory]
    [InlineData(@"in C:\logs\app.log, line 4", @"C:\logs\app.log")]
    [InlineData("see C:/Temp/Logs/log-20260209_2/log-20260211.txt", "C:/Temp/Logs/log-20260209_2/log-20260211.txt")]
    [InlineData(@"share \\server\logs\run 2 failed", @"\\server\logs\run")]
    [InlineData(@"(see D:\data\)", @"D:\data\")]
    [InlineData(@"quoted ""C:\My logs\a b.log"" here", @"C:\My logs\a b.log")]
    public void Finds_drive_and_UNC_paths_with_either_slash(string note, string expected)
    {
        var link = Assert.Single(NoteLinks.Find(note));
        Assert.Equal(expected, link.Target);
        Assert.Equal(NoteLinkKind.Path, link.Kind);
    }

    [Theory]
    [InlineData("no links here")]
    [InlineData("time 12:30:45")]
    [InlineData("ftp://not.supported")]
    [InlineData("")]
    public void Plain_text_has_no_links(string note) => Assert.Empty(Targets(note));

    private MainViewModel Start(List<ProcessStartInfo> started)
    {
        var vm = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        vm.StartProcess = started.Add;
        _owned.Add(vm);
        return vm;
    }

    private static NoteLink Link(string target) => NoteLinks.Find(target).Single();

    [Fact]
    public async Task A_file_link_selects_the_file_in_Explorer_and_open_in_tab_opens_it_in_Lignum()
    {
        var started = new List<ProcessStartInfo>();
        var vm = Start(started);
        var log = Path.Combine(_dir, "linked.log");
        File.WriteAllLines(log, ["a", "b"]);

        await vm.OpenNoteLinkAsync(Link(log.Replace('\\', '/'))); // forward slashes work too
        Assert.Equal("explorer.exe", Assert.Single(started).FileName);
        Assert.Equal($"/select,\"{log}\"", started[0].Arguments); // backslashes: Explorer needs them
        Assert.Empty(vm.Tabs);

        await vm.OpenNoteLinkAsync(Link(log), inTab: true);
        Assert.Equal("linked.log", vm.SelectedTab!.FileName);
        Assert.Single(started);
    }

    [Fact]
    public async Task A_folder_link_opens_in_Explorer()
    {
        var started = new List<ProcessStartInfo>();
        var vm = Start(started);

        await vm.OpenNoteLinkAsync(Link(_dir));

        Assert.Equal("explorer.exe", Assert.Single(started).FileName);
        Assert.Equal($"\"{_dir}\"", started[0].Arguments);
        Assert.Empty(vm.Tabs);
    }

    [Fact]
    public async Task A_missing_path_is_reported_when_clicked()
    {
        var started = new List<ProcessStartInfo>();
        var vm = Start(started);
        var missing = Path.Combine(_dir, "gone.log");

        await vm.OpenNoteLinkAsync(Link(missing));

        Assert.Empty(started);
        Assert.Empty(vm.Tabs);
        Assert.Equal($"{missing} does not exist.", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_link_opens_in_the_default_browser()
    {
        var started = new List<ProcessStartInfo>();
        var vm = UiContext.None(() => new MainViewModel(
            new SettingsStore(Path.Combine(_dir, "settings"), Logger.None).Open<AppSettings>(AppSettings.FileName)));
        vm.StartProcess = started.Add;
        _owned.Add(vm);
        var log = Path.Combine(_dir, "a.log");
        File.WriteAllLines(log, ["a"]);
        await vm.OpenFileAsync(log);

        vm.SelectedTab!.OpenNoteLinkCommand.Execute(NoteLinks.Find("https://example.com").Single());

        Assert.Equal("https://example.com", Assert.Single(started).FileName);
        Assert.True(started[0].UseShellExecute);
    }
}
