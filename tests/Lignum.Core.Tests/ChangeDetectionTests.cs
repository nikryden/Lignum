using System.Collections.Concurrent;
using System.Text;
using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

/// <summary>The document notices when the file on disk grows, is truncated or is replaced.</summary>
public class ChangeDetectionTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static void Append(string path, string text)
    {
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Collects change notifications and lets a test wait for one.</summary>
    private sealed class ChangeRecorder
    {
        private readonly BlockingCollection<FileChangedEventArgs> _changes = new();

        public ChangeRecorder(LogDocument doc) => doc.FileChanged += (_, e) => _changes.Add(e);

        public FileChangedEventArgs Next()
        {
            Assert.True(_changes.TryTake(out var change, Timeout), "No change notification arrived.");
            return change!;
        }

        public bool Any(TimeSpan wait) => _changes.TryTake(out _, wait);
    }

    [Fact]
    public async Task Appending_to_the_file_raises_grown_with_the_new_length()
    {
        var path = _temp.WriteFile("one\n");
        using var doc = LogDocument.Open(path);
        var changes = new ChangeRecorder(doc);
        await doc.StartIndexing();

        Append(path, "two\n");

        var change = changes.Next();
        Assert.Equal(FileChangeKind.Grown, change.Kind);
        Assert.Equal(8, change.Length);
        Assert.True(doc.IsIndexValid);
        Assert.Equal(1, doc.LineCount); // growth does not invalidate what is already indexed
    }

    [Fact]
    public async Task Truncating_the_file_raises_truncated_and_the_stale_lines_disappear()
    {
        var path = _temp.WriteFile("one\ntwo\nthree\n");
        using var doc = LogDocument.Open(path);
        var changes = new ChangeRecorder(doc);
        await doc.StartIndexing();
        Assert.Equal(3, doc.LineCount);

        using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            writer.SetLength(4); // "one\n"
        }

        var change = changes.Next();
        Assert.Equal(FileChangeKind.Truncated, change.Kind);
        Assert.Equal(4, change.Length);
        Assert.True(change.InvalidatesIndex);
        Assert.False(doc.IsIndexValid);
        Assert.Equal(0, doc.LineCount);
    }

    [Fact]
    public async Task Rotating_the_file_raises_replaced_even_when_the_new_file_is_larger()
    {
        var path = _temp.WriteFile("old\n");
        using var doc = LogDocument.Open(path);
        var changes = new ChangeRecorder(doc);
        await doc.StartIndexing();

        File.Move(path, _temp.File("test.log.1"));
        File.WriteAllText(path, "a much longer new file\n");

        var change = changes.Next();
        Assert.Equal(FileChangeKind.Replaced, change.Kind);
        Assert.False(doc.IsIndexValid);
        Assert.Equal(0, doc.LineCount);
    }

    [Fact]
    public async Task Changes_are_detected_while_a_writer_keeps_the_file_open()
    {
        var path = _temp.File("live.log");
        using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write(Encoding.UTF8.GetBytes("one\n"));
        writer.Flush();
        using var doc = LogDocument.Open(path);
        var changes = new ChangeRecorder(doc);
        await doc.StartIndexing();

        writer.Write(Encoding.UTF8.GetBytes("two\n"));
        writer.Flush();
        var grown = changes.Next();

        writer.SetLength(2);
        writer.Flush();
        var truncated = changes.Next();

        Assert.Equal((FileChangeKind.Grown, 8L), (grown.Kind, grown.Length));
        Assert.Equal((FileChangeKind.Truncated, 2L), (truncated.Kind, truncated.Length));
    }

    [Fact]
    public async Task Each_growth_is_reported_so_tail_mode_can_follow_the_file()
    {
        var path = _temp.WriteFile("one\n");
        using var doc = LogDocument.Open(path);
        var changes = new ChangeRecorder(doc);
        await doc.StartIndexing();

        Append(path, "two\n");
        var first = changes.Next();
        Append(path, "three\n");
        var second = changes.Next();

        Assert.Equal(8, first.Length);
        Assert.Equal(14, second.Length);
        Assert.True(doc.IsIndexValid);
    }

    [Fact]
    public async Task An_unchanged_file_raises_nothing()
    {
        var path = _temp.WriteFile("still\n");
        using var doc = LogDocument.Open(path);
        var changes = new ChangeRecorder(doc);
        await doc.StartIndexing();

        File.SetLastAccessTimeUtc(path, DateTime.UtcNow); // noise that must not count as a change
        doc.CheckForChanges();

        Assert.False(changes.Any(TimeSpan.FromSeconds(1.5)));
        Assert.True(doc.IsIndexValid);
    }

    [Fact]
    public async Task Deleting_the_file_raises_replaced()
    {
        var path = _temp.WriteFile("doomed\n");
        using var doc = LogDocument.Open(path);
        var changes = new ChangeRecorder(doc);
        await doc.StartIndexing();

        File.Delete(path);

        var change = changes.Next();
        Assert.Equal(FileChangeKind.Replaced, change.Kind);
        Assert.Equal(0, change.Length);
        Assert.False(doc.IsIndexValid);
    }
}
