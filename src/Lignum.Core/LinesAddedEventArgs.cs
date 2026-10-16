namespace Lignum.Core;

/// <summary>Raised (on a background thread) when indexing has made more lines available.</summary>
public sealed class LinesAddedEventArgs(long lineCount, long bytesIndexed, long totalBytes) : EventArgs
{
    /// <summary>The line count after this batch.</summary>
    public long LineCount { get; } = lineCount;

    /// <summary>How many bytes of the file have been indexed so far.</summary>
    public long BytesIndexed { get; } = bytesIndexed;

    /// <summary>The file length indexing is working towards.</summary>
    public long TotalBytes { get; } = totalBytes;
}
