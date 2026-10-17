using Microsoft.Win32.SafeHandles;

namespace Lignum.Core.Watching;

/// <summary>
/// Detects growth of an open file. A <see cref="FileSystemWatcher"/> gives a quick reaction; a slow poll
/// backs it up, because Windows does not always report size changes while a writer still has the file open.
/// Every check reads the length through the document's own handle, which is always current.
/// </summary>
internal sealed class FileChangeWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly string _path;
    private readonly SafeFileHandle _handle;
    private readonly Action<FileChangeKind, long> _onChange;
    private readonly Lock _gate = new();
    private readonly FileIdentity? _identity;
    private long _knownLength;
    private DateTime _knownWriteTime;
    private FileSystemWatcher? _fsw;
    private Timer? _poll;
    private bool _stopped;

    public FileChangeWatcher(string path, SafeFileHandle handle, long knownLength, Action<FileChangeKind, long> onChange)
    {
        _path = path;
        _handle = handle;
        _knownLength = knownLength;
        _onChange = onChange;
        _identity = FileIdentity.Of(handle);
        _knownWriteTime = LastWriteTime();
    }

    public void Start()
    {
        try
        {
            var fsw = new FileSystemWatcher(System.IO.Path.GetDirectoryName(_path)!, System.IO.Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.FileName
                               | NotifyFilters.CreationTime,
            };
            fsw.Changed += (_, _) => Check();
            fsw.Created += (_, _) => Check();
            fsw.Deleted += (_, _) => Check();
            fsw.Renamed += (_, _) => Check();
            fsw.EnableRaisingEvents = true;
            _fsw = fsw;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException
                                       or PlatformNotSupportedException)
        {
            // Some folders (network shares, odd file systems) cannot be watched; the poll still works.
        }

        _poll = new Timer(_ => Check(), null, PollInterval, PollInterval);
    }

    /// <summary>Compares the file with what was last seen and reports a change. Never throws.</summary>
    public void Check()
    {
        lock (_gate)
        {
            if (_stopped) return;
            try
            {
                if (!PathStillRefersToOurFile(out var lengthAtPath))
                {
                    Stop(FileChangeKind.Replaced, lengthAtPath);
                    return;
                }

                var length = RandomAccess.GetLength(_handle);
                var writeTime = LastWriteTime();
                if (length < _knownLength)
                {
                    Stop(FileChangeKind.Truncated, length);
                }
                else if (length > _knownLength)
                {
                    _knownLength = length;
                    _knownWriteTime = writeTime;
                    _onChange(FileChangeKind.Grown, length);
                }
                else if (writeTime > _knownWriteTime)
                {
                    // Same length but written since: the content was rewritten in place, so the index is stale.
                    Stop(FileChangeKind.Replaced, length);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
            {
                // Transient (or the document is closing); the next check tries again.
            }
        }
    }

    /// <summary>
    /// False when nothing is at the path any more, or (on Windows) a different file is. Errors that say
    /// nothing about identity (sharing violations, access denied, e.g. while a delete is pending) count as
    /// "still ours" so a busy writer never causes a false alarm.
    /// </summary>
    private bool PathStillRefersToOurFile(out long lengthAtPath)
    {
        lengthAtPath = 0;
        SafeFileHandle atPath;
        try
        {
            atPath = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        using (atPath)
        {
            if (_identity is not { } ours) return true;
            if (FileIdentity.Of(atPath) is not { } theirs || theirs == ours) return true;
            lengthAtPath = RandomAccess.GetLength(atPath);
            return false;
        }
    }

    /// <summary>Reports a change that invalidates the index; nothing is reported after it.</summary>
    private DateTime LastWriteTime() => File.GetLastWriteTimeUtc(_handle);

    private void Stop(FileChangeKind kind, long length)
    {
        _stopped = true;
        _onChange(kind, length);
    }

    public void Dispose()
    {
        lock (_gate) _stopped = true;
        _fsw?.Dispose();
        _poll?.Dispose();
    }
}
