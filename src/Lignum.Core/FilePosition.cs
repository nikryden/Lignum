namespace Lignum.Core;

/// <summary>
/// A position in the file's text: a 0-based line and a 0-based column, the gap before character
/// <see cref="Column"/> of the line's decoded text (UTF-16 code units, tabs count as one). Independent of
/// what any view shows. Ordered by line, then column.
/// </summary>
public readonly record struct FilePosition(long Line, int Column) : IComparable<FilePosition>
{
    /// <summary>The start of the file.</summary>
    public static FilePosition Start => default;

    public int CompareTo(FilePosition other)
    {
        var byLine = Line.CompareTo(other.Line);
        return byLine != 0 ? byLine : Column.CompareTo(other.Column);
    }

    public static bool operator <(FilePosition a, FilePosition b) => a.CompareTo(b) < 0;
    public static bool operator >(FilePosition a, FilePosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(FilePosition a, FilePosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(FilePosition a, FilePosition b) => a.CompareTo(b) >= 0;
}
