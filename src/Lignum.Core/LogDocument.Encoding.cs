using Lignum.Core.Reading;
using System.Text;
using Lignum.Core.Text;

namespace Lignum.Core;

// Encoding: detected (or forced by the caller) at the start of the indexing pass, before any line exists.
// All decoding and line-break handling goes through Codec (Text/TextCodec), so readers that come later
// (text ranges, search, long lines) work for every encoding without knowing which one it is.
public sealed partial class LogDocument
{
    private readonly LogEncoding? _requestedEncoding;
    private TextCodec? _codec;

    /// <summary>
    /// The file's encoding: detected from its start, or the one passed to <see cref="Open(string, LogEncoding?)"/>.
    /// Null until indexing has started and examined the file (it is known before the first lines are added).
    /// </summary>
    public LogEncoding? FileEncoding => Volatile.Read(ref _codec)?.Kind;

    /// <summary>Whether the file starts with a byte order mark for <see cref="FileEncoding"/> (it is never shown as text).</summary>
    public bool HasByteOrderMark => Volatile.Read(ref _codec)?.HasByteOrderMark ?? false;

    /// <summary>True when the encoding was chosen by the caller rather than detected.</summary>
    public bool IsEncodingOverridden => _requestedEncoding is not null;

    /// <summary>
    /// The decoder and line-break rules for this file. Before detection it is plain UTF-8, which is harmless
    /// because no line exists yet.
    /// </summary>
    internal TextCodec Codec => Volatile.Read(ref _codec) ?? TextCodec.Utf8NoBom;

    /// <summary>The file's .NET encoding (no preamble), for callers that need an <see cref="Encoding"/>.</summary>
    private Encoding TextEncoding => Codec.Encoding;

    /// <summary>A stateful decoder for streaming (characters may straddle chunk edges).</summary>
    private Decoder CreateDecoder() => Codec.CreateDecoder();

    private string Decode(ReadOnlySpan<byte> bytes) => Codec.Decode(bytes);

    /// <summary>Reads the start of the file and fixes the codec. Runs on the indexer thread.</summary>
    private TextCodec DetectEncoding()
    {
        var sample = new byte[(int)Math.Min(EncodingDetector.SampleSize, TotalBytes)];
        var filled = FileReads.ReadFully(_handle, sample, 0);

        var bytes = sample.AsSpan(0, filled);
        var codec = _requestedEncoding is { } forced
            ? EncodingDetector.ForOverride(forced, bytes)
            : EncodingDetector.Detect(bytes, isWholeFile: filled >= TotalBytes);
        Volatile.Write(ref _codec, codec);
        return codec;
    }
}
