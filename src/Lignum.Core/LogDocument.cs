using Lignum.Core.Indexing;
using Microsoft.Win32.SafeHandles;

namespace Lignum.Core;

/// <summary>
/// One open log file: the single public entry point to Core. Opening is cheap; the line index is
/// built by a background pass started with <see cref="StartIndexing"/>, which can be observed
/// (<see cref="LineCount"/>, <see cref="LinesAdded"/>, <see cref="IndexingCompletion"/>) and cancelled.
/// The file is opened read-only with <c>FileShare.ReadWrite | FileShare.Delete</c> so writers can keep
/// appending to, renaming and deleting it.
/// </summary>
/// <remarks>
/// Line rules: only <c>\n</c> and <c>\r\n</c> end a line (a lone <c>\r</c> is an ordinary character).
/// A trailing newline does not start an extra empty line; text after the last newline is a line.
/// An empty file has no lines. Encoding is UTF-8 for now.
/// </remarks>
public sealed partial class LogDocument : IDisposable
{
    private const int ReadBufferSize = 1 << 20;

    private readonly SafeFileHandle _handle;
    private readonly LineIndex _index = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _bytesIndexed;
    private int _state = (int)IndexingState.NotStarted;
    private int _disposed;

    private LogDocument(string path, SafeFileHandle handle, long length)
    {
        Path = path;
        _handle = handle;
        TotalBytes = length;
    }

    /// <summary>
    /// Opens <paramref name="path"/> read-only without blocking other processes. Call
    /// <see cref="StartIndexing"/> (after subscribing to <see cref="LinesAdded"/>) to build the index.
    /// Throws the usual file exceptions if the file cannot be opened.
    /// </summary>
    public static LogDocument Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        var handle = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        try
        {
            return new LogDocument(fullPath, handle, RandomAccess.GetLength(handle));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>The full path the document was opened from.</summary>
    public string Path { get; }

    /// <summary>The file name without its folder, for display.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>File length when it was opened; indexing runs up to this length.</summary>
    public long TotalBytes { get; }

    /// <summary>Bytes indexed so far. Thread-safe.</summary>
    public long BytesIndexed => Interlocked.Read(ref _bytesIndexed);

    /// <summary>Indexing progress between 0 and 1. Thread-safe.</summary>
    public double IndexingProgress => TotalBytes == 0 ? 1.0 : (double)BytesIndexed / TotalBytes;

    /// <summary>Lines indexed so far. Rises while indexing runs. Thread-safe.</summary>
    public long LineCount => _index.Count;

    /// <summary>Current indexing state. Thread-safe.</summary>
    public IndexingState IndexingState => (IndexingState)Volatile.Read(ref _state);

    /// <summary>The error that stopped indexing, when <see cref="IndexingState"/> is <c>Failed</c>.</summary>
    public Exception? IndexingError { get; private set; }

    /// <summary>
    /// Completes when indexing stops for any reason (completed, cancelled or failed). Never faults;
    /// check <see cref="IndexingState"/> afterwards.
    /// </summary>
    public Task IndexingCompletion => _completion.Task;

    /// <summary>
    /// Raised on a background thread each time indexing makes more of the file available.
    /// Handlers must be quick and marshal to the UI thread themselves.
    /// </summary>
    public event EventHandler<LinesAddedEventArgs>? LinesAdded;

    /// <summary>
    /// Starts the background indexing pass and returns <see cref="IndexingCompletion"/>.
    /// Calling it again has no effect.
    /// </summary>
    public Task StartIndexing()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _state, (int)IndexingState.Running, (int)IndexingState.NotStarted)
            == (int)IndexingState.NotStarted)
        {
            Task.Run(RunIndexing);
        }

        return IndexingCompletion;
    }

    /// <summary>Stops indexing. Lines already indexed stay available.</summary>
    public void CancelIndexing() => _cancel.Cancel();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancel.Cancel();
        if (Interlocked.CompareExchange(ref _state, (int)IndexingState.Cancelled, (int)IndexingState.NotStarted)
            == (int)IndexingState.NotStarted)
        {
            _completion.TrySetResult();
        }

        // Never block the caller on file I/O: the handle is released once the indexer has stopped.
        IndexingCompletion.ContinueWith(_ =>
        {
            _handle.Dispose();
            _cancel.Dispose();
        }, TaskScheduler.Default);
    }

    private void RunIndexing()
    {
        var token = _cancel.Token;
        try
        {
            var buffer = new byte[ReadBufferSize];
            var offset = 0L;
            var atLineStart = true;
            while (offset < TotalBytes)
            {
                token.ThrowIfCancellationRequested();
                var toRead = (int)Math.Min(buffer.Length, TotalBytes - offset);
                var read = RandomAccess.Read(_handle, buffer.AsSpan(0, toRead), offset);
                if (read == 0) break; // file shrank under us; change detection deals with that later

                atLineStart = IndexBlock(buffer.AsSpan(0, read), offset, atLineStart);
                offset += read;
                Interlocked.Exchange(ref _bytesIndexed, offset);
                LinesAdded?.Invoke(this, new LinesAddedEventArgs(_index.Count, offset, TotalBytes));
            }

            Finish(IndexingState.Completed);
        }
        catch (OperationCanceledException)
        {
            Finish(IndexingState.Cancelled);
        }
        catch (ObjectDisposedException) when (token.IsCancellationRequested)
        {
            Finish(IndexingState.Cancelled);
        }
        catch (Exception ex)
        {
            IndexingError = ex;
            Finish(IndexingState.Failed);
        }
    }

    /// <summary>Records the line starts in one block. Returns whether the next byte starts a line.</summary>
    private bool IndexBlock(ReadOnlySpan<byte> block, long blockOffset, bool atLineStart)
    {
        if (atLineStart) _index.Append(blockOffset);

        var pos = 0;
        while (true)
        {
            var hit = block[pos..].IndexOf((byte)'\n');
            if (hit < 0) return false;
            pos += hit + 1;
            if (pos == block.Length) return true; // the next line, if any, starts in the next block
            _index.Append(blockOffset + pos);
        }
    }

    private void Finish(IndexingState state)
    {
        Volatile.Write(ref _state, (int)state);
        _completion.TrySetResult();
    }
}
