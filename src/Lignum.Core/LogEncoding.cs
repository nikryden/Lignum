namespace Lignum.Core;

/// <summary>The text encodings Lignum reads. A byte order mark, if present, is reported separately.</summary>
public enum LogEncoding
{
    Utf8,
    Utf16LE,
    Utf16BE,
    Windows1252,
}

public static class LogEncodingExtensions
{
    /// <summary>Short name for the status bar, e.g. "UTF-8", "UTF-8 BOM", "UTF-16 LE".</summary>
    public static string DisplayName(this LogEncoding encoding, bool withByteOrderMark = false) => encoding switch
    {
        LogEncoding.Utf8 => withByteOrderMark ? "UTF-8 BOM" : "UTF-8",
        LogEncoding.Utf16LE => "UTF-16 LE",
        LogEncoding.Utf16BE => "UTF-16 BE",
        LogEncoding.Windows1252 => "Windows-1252",
        _ => encoding.ToString(),
    };
}
