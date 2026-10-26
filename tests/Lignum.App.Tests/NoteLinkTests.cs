using System.Diagnostics;
using Lignum.App.Settings;
using Lignum.App.Tests.Support;
using Lignum.App.ViewModels;
using Serilog.Core;

namespace Lignum.App.Tests;

/// <summary>Web links in bookmark notes (ticket 23).</summary>
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
    [InlineData("no links here")]
    [InlineData(@"in C:\logs\app.log")]
    [InlineData(@"\\server\share\run.log")]
    [InlineData("ftp://not.supported")]
    [InlineData("")]
    public void Paths_and_plain_text_are_not_links(string note) => Assert.Empty(Targets(note));

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
