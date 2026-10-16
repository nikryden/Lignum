namespace Lignum.Core.Indexing;

/// <summary>
/// Byte offsets where lines start, kept in memory only. Append-only from one writer thread;
/// safe to read from other threads (readers see a consistent prefix). Stored in fixed-size
/// chunks so appending never copies existing offsets, which keeps it ready for tail mode.
/// </summary>
internal sealed class LineIndex
{
    private const int ChunkBits = 16;
    private const int ChunkSize = 1 << ChunkBits;
    private const int ChunkMask = ChunkSize - 1;

    private long[][] _chunks = [new long[ChunkSize]];
    private long _count;

    /// <summary>Number of line starts recorded.</summary>
    public long Count => Volatile.Read(ref _count);

    public long this[long index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            var chunks = Volatile.Read(ref _chunks);
            return chunks[index >> ChunkBits][index & ChunkMask];
        }
    }

    /// <summary>Appends a line start. Single writer only.</summary>
    public void Append(long offset)
    {
        var count = _count;
        var chunkIndex = (int)(count >> ChunkBits);
        var chunks = _chunks;
        if (chunkIndex == chunks.Length)
        {
            var grown = new long[chunks.Length * 2][];
            Array.Copy(chunks, grown, chunks.Length);
            Volatile.Write(ref _chunks, grown);
            chunks = grown;
        }

        chunks[chunkIndex] ??= new long[ChunkSize];
        chunks[chunkIndex][count & ChunkMask] = offset;
        Volatile.Write(ref _count, count + 1);
    }
}
