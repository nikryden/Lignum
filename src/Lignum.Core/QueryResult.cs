using Lignum.Core.Indexing;

namespace Lignum.Core;

/// <summary>How far a <see cref="QueryResult"/> got.</summary>
public enum QueryState
{
    Running,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>
/// The lines matching a <see cref="Query"/>: an ordered, growing list of 0-based line numbers. It fills in
/// on background threads (in file order) while the query runs, including while the document is still being
/// indexed. All members are thread-safe. Disposing cancels the query.
/// </summary>
public sealed class QueryResult : IDisposable
{
    private readonly LineIndex _lines = new(); // an append-only list of longs; here, matching line numbers
    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _bytesSearched;
    private int _state = (int)QueryState.Running;

    internal QueryResult(Query query, long totalBytes)
    {
        Query = query;
        TotalBytes = totalBytes;
    }

    public Query Query { get; }

    /// <summary>Matching lines found so far.</summary>
    public long Count => _lines.Count;

    /// <summary>The 0-based file line number of match <paramref name="row"/>.</summary>
    public long this[long row] => _lines[row];

    public QueryState State => (QueryState)Volatile.Read(ref _state);

    /// <summary>The error that stopped the query when <see cref="State"/> is <c>Failed</c> (e.g. a regex timeout).</summary>
    public Exception? Error { get; private set; }

    /// <summary>Share of the file searched, 0..1. Reaches 1 when the query completes.</summary>
    public double Progress => State == QueryState.Completed || TotalBytes == 0
        ? 1.0
        : Math.Min(1.0, (double)Interlocked.Read(ref _bytesSearched) / TotalBytes);

    /// <summary>Completes when the query stops for any reason. Never faults; check <see cref="State"/>.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Raised on a background thread when more matches arrive or the query stops. Marshal yourself.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The first row whose line number is at least <paramref name="line"/>, or <see cref="Count"/> when there is
    /// none. Use it to step to the next (or, minus one, the previous) match from a line.
    /// </summary>
    public long FindRow(long line)
    {
        long lo = 0, hi = Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_lines[mid] < line) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    /// <summary>Stops the query. Matches found so far stay.</summary>
    public void Cancel()
    {
        try { _cancel.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => Cancel();

    internal long TotalBytes { get; }

    internal CancellationToken Token => _cancel.Token;

    internal void Append(List<long> lines, long bytesSearched)
    {
        foreach (var line in lines) _lines.Append(line);
        Interlocked.Exchange(ref _bytesSearched, bytesSearched);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Finish(QueryState state, Exception? error = null)
    {
        Error = error;
        Volatile.Write(ref _state, (int)state);
        Changed?.Invoke(this, EventArgs.Empty);
        _completion.TrySetResult();
    }
}
