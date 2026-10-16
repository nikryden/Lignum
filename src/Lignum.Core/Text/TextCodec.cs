using System.Text;

namespace Lignum.Core.Text;

/// <summary>
/// Everything that depends on a file's encoding, in one place: decoding bytes to text, finding line breaks
/// in raw bytes (1-byte units for UTF-8/1252, 2-byte units for UTF-16) and the byte order mark to skip.
/// Immutable; one per indexed document. Every reader (lines, text ranges, search) must decode and trim line
/// breaks through this rather than assume UTF-8.
/// </summary>
internal sealed class TextCodec
{
    private static readonly Encoding Windows1252Encoding =
        CodePagesEncodingProvider.Instance.GetEncoding(1252)
        ?? throw new InvalidOperationException("Windows-1252 is not available.");

    public static readonly TextCodec Utf8NoBom = new(LogEncoding.Utf8, 0);

    public TextCodec(LogEncoding kind, int preambleLength)
    {
        Kind = kind;
        PreambleLength = preambleLength;
        (Encoding, UnitSize, MaxBytesPerChar) = kind switch
        {
            LogEncoding.Utf8 => ((Encoding)new UTF8Encoding(false), 1, 3),
            LogEncoding.Utf16LE => (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 2, 2),
            LogEncoding.Utf16BE => (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 2, 2),
            LogEncoding.Windows1252 => (Windows1252Encoding, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public LogEncoding Kind { get; }

    /// <summary>Bytes of byte order mark at the start of the file; line 0 starts after them.</summary>
    public int PreambleLength { get; }

    public bool HasByteOrderMark => PreambleLength > 0;

    /// <summary>The .NET encoding (no preamble). Prefer the helpers below.</summary>
    public Encoding Encoding { get; }

    /// <summary>Size of one code unit in bytes: 1, or 2 for UTF-16. Line breaks are one unit.</summary>
    public int UnitSize { get; }

    /// <summary>Most bytes one UTF-16 <see cref="char"/> can take in this encoding (for "read N chars" limits).</summary>
    public int MaxBytesPerChar { get; }

    public string Decode(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes);

    /// <summary>Decodes into <paramref name="chars"/>, which needs room for <paramref name="bytes"/>.Length chars.</summary>
    public int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars) => Encoding.GetChars(bytes, chars);

    /// <summary>A stateful decoder for streaming (characters may straddle chunk edges).</summary>
    public Decoder CreateDecoder() => Encoding.GetDecoder();

    /// <summary>
    /// Length of <paramref name="line"/> without a trailing <c>\n</c> or <c>\r\n</c> (in this encoding's units).
    /// </summary>
    public int TrimLineBreak(ReadOnlySpan<byte> line)
    {
        var end = line.Length;
        if (IsUnitAt(line, end - UnitSize, '\n'))
        {
            end -= UnitSize;
            if (IsUnitAt(line, end - UnitSize, '\r')) end -= UnitSize;
        }

        return end;
    }

    /// <summary>
    /// Index just past the first <c>\n</c> unit in <paramref name="bytes"/>, or -1. <paramref name="bytes"/>
    /// must start on a code-unit boundary.
    /// </summary>
    public int IndexAfterNewline(ReadOnlySpan<byte> bytes)
    {
        if (UnitSize == 1)
        {
            var hit = bytes.IndexOf((byte)'\n');
            return hit < 0 ? -1 : hit + 1;
        }

        var pos = 0;
        while (true)
        {
            var hit = bytes[pos..].IndexOf((byte)'\n');
            if (hit < 0) return -1;
            var at = pos + hit;
            // Only a unit that sits on an even offset and whose other byte is 0 is a newline.
            var unitStart = at - (Kind == LogEncoding.Utf16LE ? 0 : 1);
            if (unitStart >= 0 && unitStart % 2 == 0 && unitStart + 2 <= bytes.Length && IsUnitAt(bytes, unitStart, '\n'))
                return unitStart + 2;
            pos = at + 1;
        }
    }

    /// <summary>Whether the code unit at <paramref name="offset"/> is the ASCII character <paramref name="c"/>.</summary>
    public bool IsUnitAt(ReadOnlySpan<byte> bytes, int offset, char c)
    {
        if (offset < 0 || offset + UnitSize > bytes.Length) return false;
        return Kind switch
        {
            LogEncoding.Utf16LE => bytes[offset] == c && bytes[offset + 1] == 0,
            LogEncoding.Utf16BE => bytes[offset] == 0 && bytes[offset + 1] == c,
            _ => bytes[offset] == c,
        };
    }
}
