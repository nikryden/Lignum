using Microsoft.Win32.SafeHandles;

namespace Lignum.Core.Reading;

/// <summary>
/// A small LRU cache of fixed-size file blocks, filled by positional reads (<see cref="RandomAccess"/>).
/// Thread-safe; file I/O happens outside the lock. A cached block shorter than a request (it was read
/// at the end of the file, which has since grown) is read again.
/// </summary>
internal sealed class BlockCache
{
    public const int BlockSize = 64 * 1024;

    private readonly SafeFileHandle _handle;
    private readonly int _capacity;
    private readonly Dictionary<long, LinkedListNode<Block>> _map = new();
    private readonly LinkedList<Block> _lru = new();
    private readonly Lock _lock = new();

    public BlockCache(SafeFileHandle handle, int capacity = 64)
    {
        _handle = handle;
        _capacity = capacity;
    }

    /// <summary>Total bytes the cache can hold.</summary>
    public long CapacityBytes => (long)_capacity * BlockSize;

    /// <summary>
    /// Copies the bytes at <paramref name="offset"/> into <paramref name="destination"/>. Returns the number
    /// of bytes copied, which is less than requested only if the file ends first.
    /// </summary>
    public int Read(long offset, Span<byte> destination)
    {
        // A request larger than the cache would only evict everything; read it directly.
        if (destination.Length > CapacityBytes / 2) return ReadDirect(offset, destination);

        var copied = 0;
        while (copied < destination.Length)
        {
            var position = offset + copied;
            var blockStart = position - position % BlockSize;
            var inBlock = (int)(position - blockStart);
            var wanted = Math.Min(destination.Length - copied, BlockSize - inBlock);

            var block = GetBlock(blockStart, inBlock + wanted);
            var available = Math.Min(wanted, block.Length - inBlock);
            if (available <= 0) break;

            block.Data.AsSpan(inBlock, available).CopyTo(destination[copied..]);
            copied += available;
            if (available < wanted) break; // end of file
        }

        return copied;
    }

    private int ReadDirect(long offset, Span<byte> destination)
    {
        var copied = 0;
        while (copied < destination.Length)
        {
            var read = RandomAccess.Read(_handle, destination[copied..], offset + copied);
            if (read == 0) break;
            copied += read;
        }

        return copied;
    }

    private Block GetBlock(long blockStart, int neededLength)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(blockStart, out var node) && node.Value.Length >= neededLength)
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value;
            }
        }

        var data = new byte[BlockSize];
        var length = ReadDirect(blockStart, data);
        var block = new Block(blockStart, data, length);

        lock (_lock)
        {
            if (_map.Remove(blockStart, out var stale)) _lru.Remove(stale);
            _map[blockStart] = _lru.AddFirst(block);
            while (_lru.Count > _capacity)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Start);
            }
        }

        return block;
    }

    private sealed record Block(long Start, byte[] Data, int Length);
}
