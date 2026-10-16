using Lignum.App.Diagnostics;

namespace Lignum.App.Tests;

public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Default_log_folder_is_Lignum_logs_under_local_app_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lignum", "logs");

        Assert.Equal(expected, DiagnosticLog.DefaultDirectory);
    }

    [Fact]
    public void Starting_the_diagnostic_log_writes_a_startup_entry_to_a_file_in_the_folder()
    {
        using (var log = DiagnosticLog.Start(_dir))
        {
        }

        var file = Assert.Single(Directory.GetFiles(_dir, "lignum-*.log"));
        Assert.Contains("Lignum starting", ReadShared(file));
    }

    internal static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
