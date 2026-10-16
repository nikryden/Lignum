using Lignum.Core.Watching;

namespace Lignum.Core;

// Change detection: watching starts with StartIndexing and stops on Dispose or once the index is invalid.
public sealed partial class LogDocument
{
    private FileChangeWatcher? _watcher;
    private int _indexInvalid;

    /// <summary>
    /// Raised on a background thread when the file on disk changes. <see cref="FileChangeKind.Grown"/> may be
    /// raised many times (once per detected growth); <see cref="FileChangeKind.Truncated"/> or
    /// <see cref="FileChangeKind.Replaced"/> at most once, after which the document stops watching.
    /// Handlers must be quick and marshal to the UI thread themselves.
    /// </summary>
    public event EventHandler<FileChangedEventArgs>? FileChanged;

    /// <summary>
    /// False once the file has been truncated or replaced. The document then reports no lines and must be
    /// reopened to see the file's current content. Thread-safe.
    /// </summary>
    public bool IsIndexValid => Volatile.Read(ref _indexInvalid) == 0;

    /// <summary>
    /// Checks the file now instead of waiting for the next file-system event or poll. Safe to call from any
    /// thread; raises <see cref="FileChanged"/> synchronously if something changed.
    /// </summary>
    public void CheckForChanges() => _watcher?.Check();

    private void StartWatching()
    {
        _watcher = new FileChangeWatcher(Path, _handle, TotalBytes, OnFileChanged);
        _watcher.Start();
    }

    private void StopWatching() => _watcher?.Dispose();

    private void OnFileChanged(FileChangeKind kind, long length)
    {
        if (kind != FileChangeKind.Grown)
        {
            if (Interlocked.Exchange(ref _indexInvalid, 1) != 0) return;
            _watcher?.Dispose(); // nothing more to report: the document has to be reopened
            try { _cancel.Cancel(); } // indexing the rest of a file that is no longer there is pointless
            catch (ObjectDisposedException) { } // the document closed meanwhile
        }

        FileChanged?.Invoke(this, new FileChangedEventArgs(kind, length));
    }
}
