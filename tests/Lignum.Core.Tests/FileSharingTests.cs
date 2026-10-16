using System.Text;
using Lignum.Core.Tests.Support;

namespace Lignum.Core.Tests;

/// <summary>An open document must never get in the way of the process writing the log.</summary>
public class FileSharingTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Another_handle_can_append_while_the_document_is_open()
    {
        var path = _temp.WriteFile("first\n");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            writer.Write(Encoding.UTF8.GetBytes("second\n"));
        }

        Assert.Equal("first\nsecond\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Another_handle_can_rename_the_file_while_the_document_is_open()
    {
        var path = _temp.WriteFile("rotate me\n");
        var rotated = _temp.File("test.log.1");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        File.Move(path, rotated);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(rotated));
    }

    [Fact]
    public async Task Another_handle_can_delete_the_file_while_the_document_is_open()
    {
        var path = _temp.WriteFile("delete me\n");
        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        File.Delete(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_file_held_open_by_a_writer_can_be_opened_and_indexed()
    {
        var path = _temp.File("live.log");
        using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write(Encoding.UTF8.GetBytes("one\ntwo\n"));
        writer.Flush();

        using var doc = LogDocument.Open(path);
        await doc.StartIndexing();

        Assert.Equal(2, doc.LineCount);
        writer.Write(Encoding.UTF8.GetBytes("three\n")); // the writer is not disturbed
        writer.Flush();
    }
}
