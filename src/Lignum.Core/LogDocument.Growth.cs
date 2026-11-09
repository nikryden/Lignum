namespace Lignum.Core;

// Growth (tail mode): after the indexing pass, the bytes appended since are indexed on request, continuing
// from where the index ends, so a large file never needs a full pass again to show new lines.
public sealed partial class LogDocument
{
    private readonly Lock _growthLock = new();
    private Task _growth = Task.CompletedTask; // the last requested extension; extensions run one at a time

    // The last bytes indexed, as they were on disk. Growth is only trusted as an append if they are unchanged:
    // an editor that rewrites the file in place (and makes it longer) would otherwise shift every new line.
    private const int TailSampleSize = 4096;
    private byte[] _tailSample = [];

    /// <summary>
    /// Indexes the bytes appended to the file since it was last indexed, on a background thread, and returns
    /// how many lines were added (0 if none). <see cref="TotalBytes"/> rises to the new length, and
    /// <see cref="LinesAdded"/> is raised as for the indexing pass. A last line that had no line break yet
    /// grows in place. Waits for the indexing pass first, and does nothing if that pass did not complete, the
    /// index is invalid (truncated or replaced) or the document is disposed. Calls run one after another.
    /// A read error or cancellation leaves the index as far as it got.
    /// </summary>
    public Task<long> IndexAppendedAsync()
    {
        lock (_growthLock)
        {
            if (Volatile.Read(ref _disposed) != 0) return Task.FromResult(0L);
            var next = _growth.ContinueWith(_ => IndexingCompletion, TaskScheduler.Default).Unwrap()
                .ContinueWith(_ => IndexAppended(), TaskScheduler.Default);
            _growth = next;
            return next;
        }
    }

    /// <summary>Completes once no extension is running or queued; used to release the handle on dispose.</summary>
    private Task GrowthIdle()
    {
        lock (_growthLock) return _growth;
    }

    private long IndexAppended()
    {
        if (IndexingState != IndexingState.Completed || !IsIndexValid || Volatile.Read(ref _disposed) != 0) return 0;

        var token = _cancel.Token;
        var before = _index.Count;
        try
        {
            var codec = Codec;
            var length = RandomAccess.GetLength(_handle);
            var from = BytesIndexed;

            // A stray odd byte at the old end of a UTF-16 file was indexed on its own; start again from the
            // code unit it belongs to. It held no line break, so no line start is recorded twice.
            from -= (from - codec.PreambleLength) % codec.UnitSize;
            if (length <= from) return 0;

            if (!TailIsUnchanged())
            {
                // Not an append: the index no longer matches the file. Report it like a replaced file.
                OnFileChanged(FileChangeKind.Replaced, length);
                return 0;
            }

            Interlocked.Exchange(ref _totalBytes, Math.Max(length, TotalBytes));
            _atLineStart = IndexRange(codec, from, length, _atLineStart, token);
            SampleTail();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Cancelled, closed or unreadable: what was indexed stays; change detection reports a broken file.
        }

        return _index.Count - before;
    }

    /// <summary>Remembers the last bytes indexed so far. Indexer thread only.</summary>
    private void SampleTail() => _tailSample = ReadTail(BytesIndexed);

    private bool TailIsUnchanged() => ReadTail(BytesIndexed - _tailSample.Length, _tailSample.Length).AsSpan()
        .SequenceEqual(_tailSample);

    private byte[] ReadTail(long end) => ReadTail(Math.Max(0, end - TailSampleSize), (int)Math.Min(end, TailSampleSize));

    // Straight from the file, not the block cache: a cached block would still hold the old content.
    private byte[] ReadTail(long start, int length)
    {
        var bytes = new byte[length];
        var read = Reading.FileReads.ReadFully(_handle, bytes, start);
        return read == length ? bytes : bytes[..read];
    }
}
