using System.Collections.Concurrent;
using System.Text;
using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

public class IndexingTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>About 8 MB of 100-byte lines: big enough to be indexed in several steps.</summary>
    private string WriteLargeLog(out long lineCount)
    {
        var line = Encoding.UTF8.GetBytes(new string('a', 98) + "\r\n");
        lineCount = 80_000;
        var bytes = new byte[line.Length * lineCount];
        for (var i = 0; i < lineCount; i++) line.CopyTo(bytes, i * line.Length);
        return _temp.WriteBytes(bytes, "large.log");
    }

    [Fact]
    public void Opening_does_not_index_until_started()
    {
        using var doc = LogDocument.Open(_temp.WriteFile("a\nb\n"));

        Assert.Equal(IndexingState.NotStarted, doc.IndexingState);
        Assert.Equal(0, doc.LineCount);
        Assert.Equal("test.log", doc.FileName);
        Assert.Equal(4, doc.TotalBytes);
    }

    [Fact]
    public async Task Line_count_rises_while_indexing_runs_and_reports_progress()
    {
        var path = WriteLargeLog(out var expectedLines);
        using var doc = LogDocument.Open(path);
        var events = new ConcurrentQueue<LinesAddedEventArgs>();
        doc.LinesAdded += (_, e) => events.Enqueue(e);

        await doc.StartIndexing().WaitAsync(Timeout);

        Assert.Equal(IndexingState.Completed, doc.IndexingState);
        Assert.Equal(expectedLines, doc.LineCount);
        Assert.Equal(1.0, doc.IndexingProgress);

        var seen = events.ToList();
        Assert.True(seen.Count > 1, $"expected several progress events, got {seen.Count}");
        Assert.True(seen[0].LineCount > 0 && seen[0].LineCount < expectedLines);
        Assert.Equal(seen.Select(e => e.LineCount).Order(), seen.Select(e => e.LineCount));
        Assert.Equal(seen.Select(e => e.BytesIndexed).Order(), seen.Select(e => e.BytesIndexed));
        Assert.All(seen, e => Assert.Equal(doc.TotalBytes, e.TotalBytes));
        Assert.Equal(expectedLines, seen[^1].LineCount);
        Assert.Equal(doc.TotalBytes, seen[^1].BytesIndexed);
    }

    [Fact]
    public async Task Indexing_runs_off_the_calling_thread()
    {
        var path = WriteLargeLog(out _);
        using var doc = LogDocument.Open(path);
        var callerThread = Environment.CurrentManagedThreadId;
        var indexerThreads = new ConcurrentBag<int>();
        doc.LinesAdded += (_, _) => indexerThreads.Add(Environment.CurrentManagedThreadId);

        await doc.StartIndexing().WaitAsync(Timeout);

        Assert.NotEmpty(indexerThreads);
        Assert.DoesNotContain(callerThread, indexerThreads);
    }

    [Fact]
    public async Task Cancelling_stops_indexing_and_keeps_lines_found_so_far()
    {
        var path = WriteLargeLog(out var totalLines);
        using var doc = LogDocument.Open(path);
        var eventsAfterCancel = 0;
        var cancelled = false;
        doc.LinesAdded += (_, _) =>
        {
            if (cancelled) eventsAfterCancel++;
            cancelled = true;
            doc.CancelIndexing();
        };

        await doc.StartIndexing().WaitAsync(Timeout);

        Assert.Equal(IndexingState.Cancelled, doc.IndexingState);
        Assert.Equal(0, eventsAfterCancel);
        Assert.InRange(doc.LineCount, 1, totalLines - 1);
        Assert.InRange(doc.IndexingProgress, double.Epsilon, 0.999);
    }

    [Fact]
    public async Task Starting_twice_runs_one_pass()
    {
        using var doc = LogDocument.Open(_temp.WriteFile("a\nb\nc\n"));

        var first = doc.StartIndexing();
        var second = doc.StartIndexing();
        await first.WaitAsync(Timeout);
        await second.WaitAsync(Timeout);

        Assert.Equal(3, doc.LineCount);
    }

    [Fact]
    public async Task Disposing_while_indexing_cancels_it()
    {
        var path = WriteLargeLog(out _);
        var doc = LogDocument.Open(path);
        var completion = doc.StartIndexing();

        doc.Dispose();
        await completion.WaitAsync(Timeout);

        Assert.Contains(doc.IndexingState, new[] { IndexingState.Cancelled, IndexingState.Completed });
    }

    [Fact]
    public async Task Disposing_before_starting_completes_as_cancelled()
    {
        var doc = LogDocument.Open(_temp.WriteFile("a\n"));

        doc.Dispose();
        await doc.IndexingCompletion.WaitAsync(Timeout);

        Assert.Equal(IndexingState.Cancelled, doc.IndexingState);
        var startAfterDispose = Record.Exception(() => { _ = doc.StartIndexing(); });
        Assert.IsType<ObjectDisposedException>(startAfterDispose);
    }

    [Fact]
    public void Opening_a_missing_file_throws()
    {
        Assert.Throws<FileNotFoundException>(() => LogDocument.Open(_temp.File("missing.log")));
    }
}
