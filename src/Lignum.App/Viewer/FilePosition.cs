namespace Lignum.App.Viewer;

/// <summary>
/// A caret position in the file, independent of what is on screen: a 0-based file line and a 0-based
/// character index in that line's text (the gap before that character; tabs count as one character).
/// Selection (ticket 08) is a pair of these.
/// </summary>
public readonly record struct FilePosition(long Line, int Column) : IComparable<FilePosition>
{
    public static FilePosition Start => default;

    public int CompareTo(FilePosition other)
    {
        var byLine = Line.CompareTo(other.Line);
        return byLine != 0 ? byLine : Column.CompareTo(other.Column);
    }
}
