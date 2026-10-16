namespace Lignum.Core;

/// <summary>Where the background line indexing of a <see cref="LogDocument"/> is.</summary>
public enum IndexingState
{
    NotStarted,
    Running,
    Completed,
    Cancelled,
    Failed,
}
