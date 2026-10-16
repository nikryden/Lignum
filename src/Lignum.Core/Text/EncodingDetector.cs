using System.Buffers;
using System.Text.Unicode;

namespace Lignum.Core.Text;

/// <summary>
/// Guesses a file's encoding from a sample of its first bytes: byte order mark first, then UTF-8 validity,
/// then UTF-16 from the pattern of zero bytes, with Windows-1252 as the fallback.
/// </summary>
internal static class EncodingDetector
{
    /// <summary>How much of the file start is examined.</summary>
    public const int SampleSize = 64 * 1024;

    /// <summary>
    /// Detects the encoding of <paramref name="sample"/> (the start of the file). <paramref name="isWholeFile"/>
    /// says whether the sample ends at the end of the file (otherwise a cut-off UTF-8 sequence is fine).
    /// </summary>
    public static TextCodec Detect(ReadOnlySpan<byte> sample, bool isWholeFile)
    {
        var bom = PreambleLength(sample, out var bomEncoding);
        if (bom > 0) return new TextCodec(bomEncoding, bom);

        var hasZero = sample.Contains((byte)0);
        var validUtf8 = IsValidUtf8(sample, isWholeFile);
        if (validUtf8 && !hasZero) return TextCodec.Utf8NoBom;
        if (hasZero && LooksLikeUtf16(sample) is { } utf16) return new TextCodec(utf16, 0);
        if (validUtf8) return TextCodec.Utf8NoBom;
        return new TextCodec(LogEncoding.Windows1252, 0);
    }

    /// <summary>
    /// Codec for a user-chosen <paramref name="encoding"/>: that encoding's own byte order mark is skipped if
    /// the file starts with it; anything else at the start is text.
    /// </summary>
    public static TextCodec ForOverride(LogEncoding encoding, ReadOnlySpan<byte> sample)
    {
        var bom = PreambleLength(sample, out var bomEncoding);
        return new TextCodec(encoding, bom > 0 && bomEncoding == encoding ? bom : 0);
    }

    private static int PreambleLength(ReadOnlySpan<byte> sample, out LogEncoding encoding)
    {
        if (sample.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) { encoding = LogEncoding.Utf8; return 3; }
        if (sample.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) { encoding = LogEncoding.Utf16LE; return 2; }
        if (sample.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) { encoding = LogEncoding.Utf16BE; return 2; }
        encoding = default;
        return 0;
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes, bool isFinal)
    {
        var chars = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            while (true)
            {
                var status = Utf8.ToUtf16(bytes, chars, out var read, out _, replaceInvalidSequences: false,
                    isFinalBlock: isFinal);
                switch (status)
                {
                    case OperationStatus.Done: return true;
                    case OperationStatus.NeedMoreData: return true; // a sequence cut off by the sample end
                    case OperationStatus.InvalidData: return false;
                    default: bytes = bytes[read..]; break; // DestinationTooSmall: keep going
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    /// <summary>
    /// Text in UTF-16 is mostly ASCII-range characters in logs, so one byte of most code units is zero: the odd
    /// bytes for little-endian, the even bytes for big-endian.
    /// </summary>
    private static LogEncoding? LooksLikeUtf16(ReadOnlySpan<byte> sample)
    {
        var units = sample.Length / 2;
        if (units == 0) return null;
        int evenZeros = 0, oddZeros = 0;
        for (var i = 0; i + 1 < sample.Length; i += 2)
        {
            if (sample[i] == 0) evenZeros++;
            if (sample[i + 1] == 0) oddZeros++;
        }

        const double mostly = 0.4, rarely = 0.1;
        if (oddZeros >= units * mostly && evenZeros <= units * rarely) return LogEncoding.Utf16LE;
        if (evenZeros >= units * mostly && oddZeros <= units * rarely) return LogEncoding.Utf16BE;
        return null;
    }
}
