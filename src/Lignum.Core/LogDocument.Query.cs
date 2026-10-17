using Lignum.Core.Reading;
using System.Buffers;
using System.Text;
using Lignum.Core.Searching;

namespace Lignum.Core;

// Queries (find all, filters): a background pass over the indexed lines, in parallel chunks merged in order.
public sealed partial class LogDocument
{
    /// <summary>Bytes of whole lines searched by one parallel work item.</summary>
    private const int SearchChunkBytes = 2 << 20;

    /// <summary>
    /// Starts running <paramref name="query"/> on a background thread and returns its result set at once.
    /// Matches arrive in file order as the search proceeds. A query started while indexing still runs keeps
    /// going as lines are indexed and ends with the same lines as one started afterwards. It stops (cancelled)
    /// when the document is disposed or the file is truncated or replaced.
    /// </summary>
    public QueryResult StartQuery(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var result = new QueryResult(query, TotalBytes);
        Task.Run(() => RunQuery(result));
        return result;
    }

    private async Task RunQuery(QueryResult result)
    {
        var token = result.Token;
        var matcher = result.Query.Matcher;
        var moreLines = new SemaphoreSlim(0);
        void Wake() => moreLines.Release();
        void OnLinesAdded(object? sender, LinesAddedEventArgs e) => Wake();
        void OnFileChanged(object? sender, FileChangedEventArgs e) => Wake();

        LinesAdded += OnLinesAdded;
        FileChanged += OnFileChanged;
        CancellationTokenRegistration onDispose = default;
        try
        {
            onDispose = _cancel.Token.Register(result.Cancel);
            _ = IndexingCompletion.ContinueWith(_ => Wake(), TaskScheduler.Default);

            var next = 0L;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!IsIndexValid)
                {
                    result.Finish(QueryState.Cancelled);
                    return;
                }

                // Read the state before the count: once indexing has stopped, the count is final.
                var indexingDone = IndexingState is not (IndexingState.Running or IndexingState.NotStarted);
                var count = LineCount;
                // While indexing runs, the last line may still be growing: search only lines that are complete.
                var complete = indexingDone ? count : Math.Max(0, count - 1);
                if (next < complete)
                {
                    next = SearchBatch(matcher, result, next, complete, count, token);
                    continue;
                }

                if (indexingDone)
                {
                    result.Finish(QueryState.Completed);
                    return;
                }

                await moreLines.WaitAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            result.Finish(QueryState.Cancelled);
        }
        catch (ObjectDisposedException)
        {
            result.Finish(QueryState.Cancelled); // the document was closed under us
        }
        catch (Exception ex)
        {
            result.Finish(QueryState.Failed, ex);
        }
        finally
        {
            LinesAdded -= OnLinesAdded;
            FileChanged -= OnFileChanged;
            onDispose.Dispose();
        }
    }

    /// <summary>
    /// Searches lines from <paramref name="first"/> towards <paramref name="end"/> in parallel chunks, appends
    /// the matches in order and returns the line to continue from.
    /// </summary>
    private long SearchBatch(LineMatcher matcher, QueryResult result, long first, long end, long count,
        CancellationToken token)
    {
        // Cut the range into chunks of about SearchChunkBytes; a batch is a few chunks per core so matches
        // stream in steadily instead of all at the end.
        var maxChunks = Environment.ProcessorCount * 4;
        var chunks = new List<(long First, long End)>(maxChunks);
        var line = first;
        while (line < end && chunks.Count < maxChunks)
        {
            var chunkFirst = line;
            var limit = LineStart(line, count) + SearchChunkBytes;
            line++;
            while (line < end && LineStart(line + 1, count) <= limit) line++;
            chunks.Add((chunkFirst, line));
        }

        var found = new List<long>[chunks.Count];
        try
        {
            Parallel.For(0, chunks.Count, new ParallelOptions { CancellationToken = token },
                i => found[i] = SearchChunk(matcher, chunks[i].First, chunks[i].End, count));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count >= 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerExceptions[0]);
        }

        // The file may have been truncated or replaced while we read: what we got could be stale.
        if (!IsIndexValid) return first;

        var matches = new List<long>();
        foreach (var part in found) matches.AddRange(part);
        result.Append(matches, LineStart(line, count));
        return line;
    }

    /// <summary>Reads lines [first, end) in one positional read and returns the ones that match.</summary>
    private List<long> SearchChunk(LineMatcher matcher, long first, long end, long count)
    {
        var matches = new List<long>();
        var startByte = LineStart(first, count);
        var length = checked((int)(LineStart(end, count) - startByte));
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
        var chars = ArrayPool<char>.Shared.Rent(Math.Max(1, length));
        try
        {
            var filled = FileReads.ReadFully(_handle, bytes.AsSpan(0, length), startByte);

            var codec = Codec; // decoding and line breaks in the file's encoding (no encoding has more chars than bytes)
            for (var line = first; line < end; line++)
            {
                var s = (int)(LineStart(line, count) - startByte);
                var e = (int)Math.Min(filled, LineStart(line + 1, count) - startByte);
                if (s >= e) continue;
                e = s + codec.TrimLineBreak(bytes.AsSpan(s, e - s));
                var n = codec.GetChars(bytes.AsSpan(s, e - s), chars);
                if (matcher.IsMatch(chars.AsSpan(0, n))) matches.Add(line);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(chars);
        }

        return matches;
    }

    /// <summary>Byte offset where <paramref name="line"/> starts; the line after the last one starts at the indexed end.</summary>
    private long LineStart(long line, long count) => line < count ? _index[line] : BytesIndexed;
}
