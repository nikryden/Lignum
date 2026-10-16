using System.Text;
using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

public class EncodingTests : IDisposable
{
    private const string Sample = "héllo wörld ✓\r\nzweite Zeile\nlast";
    private static readonly string[] SampleLines = ["héllo wörld ✓", "zweite Zeile", "last"];

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<LogDocument> OpenIndexed(byte[] bytes, LogEncoding? encoding = null)
    {
        var doc = LogDocument.Open(_temp.WriteBytes(bytes), encoding);
        await doc.StartIndexing().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(IndexingState.Completed, doc.IndexingState);
        return doc;
    }

    private static byte[] Encode(Encoding encoding, string text, bool bom) =>
        [.. bom ? encoding.GetPreamble() : [], .. encoding.GetBytes(text)];

    [Fact]
    public async Task Detects_utf8_without_bom()
    {
        using var doc = await OpenIndexed(Encode(new UTF8Encoding(false), Sample, bom: false));

        Assert.Equal(LogEncoding.Utf8, doc.FileEncoding);
        Assert.False(doc.HasByteOrderMark);
        Assert.Equal(SampleLines, doc.ReadLines(0, 10));
    }

    public static TheoryData<string, bool, LogEncoding> Encodings => new()
    {
        { "utf-8", true, LogEncoding.Utf8 },
        { "utf-16le", true, LogEncoding.Utf16LE },
        { "utf-16le", false, LogEncoding.Utf16LE },
        { "utf-16be", true, LogEncoding.Utf16BE },
        { "utf-16be", false, LogEncoding.Utf16BE },
    };

    private static Encoding Named(string name) => name switch
    {
        "utf-8" => new UTF8Encoding(true),
        "utf-16le" => new UnicodeEncoding(false, true),
        "utf-16be" => new UnicodeEncoding(true, true),
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public async Task Detects_the_encoding_and_reads_lines_without_the_byte_order_mark(
        string name, bool bom, LogEncoding expected)
    {
        using var doc = await OpenIndexed(Encode(Named(name), Sample, bom));

        Assert.Equal(expected, doc.FileEncoding);
        Assert.Equal(bom, doc.HasByteOrderMark);
        Assert.False(doc.IsEncodingOverridden);
        Assert.Equal(SampleLines, doc.ReadLines(0, 10));
    }

    [Fact]
    public async Task Falls_back_to_windows_1252_when_the_bytes_are_not_utf8()
    {
        // "café 50€\n" in Windows-1252: é = E9, € = 80, neither valid UTF-8 on its own.
        using var doc = await OpenIndexed([.. "caf"u8, 0xE9, .. " 50"u8, 0x80, .. "\nnext"u8]);

        Assert.Equal(LogEncoding.Windows1252, doc.FileEncoding);
        Assert.Equal(["café 50€", "next"], doc.ReadLines(0, 10));
    }

    [Fact]
    public async Task An_empty_file_is_utf8_with_no_lines()
    {
        using var doc = await OpenIndexed([]);

        Assert.Equal(LogEncoding.Utf8, doc.FileEncoding);
        Assert.Equal(0, doc.LineCount);
    }

    [Theory]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    public async Task Utf16_splits_lines_only_on_whole_newline_code_units(string name)
    {
        // U+0A0A has 0x0A in both bytes and U+0D0A / U+0A0D put 0x0A next to a zero-free byte: none are breaks.
        const string text = "aਊb\r\nഊ਍\n\nlast\r\n";
        using var doc = await OpenIndexed(Encode(Named(name), text, bom: false));

        Assert.Equal(["aਊb", "ഊ਍", "", "last"], doc.ReadLines(0, 10));
    }

    [Theory]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    public async Task Utf16_lines_across_indexing_blocks_are_all_found(string name)
    {
        // Several MB, so line breaks fall on both sides of the 1 MB indexing block edges.
        var lines = Enumerable.Range(0, 120_000).Select(i => $"line {i} ünïcödé").ToArray();
        using var doc = await OpenIndexed(Encode(Named(name), string.Join("\r\n", lines) + "\r\n", bom: true));

        Assert.Equal(lines.Length, doc.LineCount);
        Assert.Equal(lines[0], doc.ReadLine(0));
        Assert.Equal(lines[54_321], doc.ReadLine(54_321));
        Assert.Equal(lines[^1], doc.ReadLine(lines.Length - 1));
    }

    [Fact]
    public async Task Overriding_the_encoding_reindexes_the_file_with_it()
    {
        var path = _temp.WriteBytes(Encode(Named("utf-16le"), "one\ntwo\n", bom: false));

        using var forced = LogDocument.Open(path, LogEncoding.Windows1252);
        await forced.StartIndexing().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(LogEncoding.Windows1252, forced.FileEncoding);
        Assert.True(forced.IsEncodingOverridden);
        // Read as single bytes, every other character is a NUL and the line break's 0x00 starts the next line.
        Assert.Equal(["o\0n\0e\0", "\0t\0w\0o\0", "\0"], forced.ReadLines(0, 10));
    }

    [Theory]
    [InlineData(LogEncoding.Utf8, new[] { "été" })]
    [InlineData(LogEncoding.Windows1252, new[] { "ï»¿Ã©tÃ©" })]
    public async Task An_override_skips_only_its_own_byte_order_mark(LogEncoding encoding, string[] expected)
    {
        using var doc = await OpenIndexed(Encode(new UTF8Encoding(true), "été", bom: true), encoding);

        Assert.Equal(encoding == LogEncoding.Utf8, doc.HasByteOrderMark);
        Assert.Equal(expected, doc.ReadLines(0, 10));
    }
}
