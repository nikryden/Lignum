namespace Lignum.App.Viewer;

/// <summary>
/// Turns mouse-wheel deltas (120 per notch, smaller steps from precision touchpads) into whole rows,
/// carrying the leftover fraction to the next event in the same direction.
/// </summary>
public sealed class WheelAccumulator
{
    private const int DeltaPerNotch = 120;

    // Pending delta scaled by lines per notch, in 1/120 row units. Positive = up (WPF's sign).
    private long _pending;

    /// <summary>Adds a wheel delta and returns rows to scroll: positive scrolls down (towards the end).</summary>
    public int Add(int delta, int linesPerNotch)
    {
        var scaled = (long)delta * linesPerNotch;
        if (_pending != 0 && Math.Sign(scaled) != Math.Sign(_pending)) _pending = 0;
        _pending += scaled;
        var rows = _pending / DeltaPerNotch;
        _pending -= rows * DeltaPerNotch;
        return (int)-rows;
    }
}
