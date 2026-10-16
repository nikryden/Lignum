using System.Text;

namespace Lignum.Core.Tests.Support;

/// <summary>A unique temp folder for real test files, deleted on dispose.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string name = "test.log") => System.IO.Path.Combine(Path, name);

    public string WriteFile(string content, string name = "test.log") =>
        WriteBytes(new UTF8Encoding(false).GetBytes(content), name);

    public string WriteBytes(byte[] bytes, string name = "test.log")
    {
        var file = File(name);
        System.IO.File.WriteAllBytes(file, bytes);
        return file;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
