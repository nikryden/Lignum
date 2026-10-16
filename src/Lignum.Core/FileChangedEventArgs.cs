namespace Lignum.Core;

/// <summary>How the file on disk differs from what the document has seen.</summary>
public enum FileChangeKind
{
    /// <summary>
    /// The file is longer than before. What is already indexed is still valid; the new bytes are
    /// not indexed (tail mode will index them).
    /// </summary>
    Grown,

    /// <summary>The file is shorter than before. The index is invalid and the file must be reloaded.</summary>
    Truncated,

    /// <summary>
    /// The path no longer refers to the opened file (it was deleted, renamed or rotated, possibly with a
    /// new file in its place). The index is invalid and the file must be reloaded.
    /// </summary>
    Replaced,
}

/// <summary>Raised (on a background thread) when the open file changes on disk.</summary>
public sealed class FileChangedEventArgs(FileChangeKind kind, long length) : EventArgs
{
    public FileChangeKind Kind { get; } = kind;

    /// <summary>
    /// The file's length now: of the opened file for <see cref="FileChangeKind.Grown"/> and
    /// <see cref="FileChangeKind.Truncated"/>, of whatever is at the path for
    /// <see cref="FileChangeKind.Replaced"/> (0 if nothing is).
    /// </summary>
    public long Length { get; } = length;

    /// <summary>True when the document's index no longer matches the file and it must be reloaded.</summary>
    public bool InvalidatesIndex => Kind != FileChangeKind.Grown;
}
