using System.Text;
using Lignum.Benchmarks;

namespace Lignum.Benchmarks.Tests;

public sealed class LogFileGeneratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lignum-tests", Guid.NewGuid().ToString("N"));

    public LogFileGeneratorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Generate(long bytes, int seed = 1)
    {
        var path = Path.Combine(_dir, $"gen-{bytes}-{seed}-{Guid.NewGuid():N}.log");
        LogFileGenerator.Generate(path, bytes, seed);
        return path;
    }

    [Fact]
    public void Generated_file_is_at_least_the_requested_size_and_ends_with_a_whole_line()
    {
        var path = Generate(200_000);

        var bytes = File.ReadAllBytes(path);
        Assert.InRange(bytes.Length, 200_000, 200_000 + LogFileGenerator.MaxLineBytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    [Fact]
    public void Same_seed_gives_the_same_file_and_another_seed_a_different_one()
    {
        var a = File.ReadAllBytes(Generate(50_000, seed: 7));
        var b = File.ReadAllBytes(Generate(50_000, seed: 7));
        var c = File.ReadAllBytes(Generate(50_000, seed: 8));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Lines_look_like_log_lines_and_include_errors_and_the_rare_word()
    {
        var lines = File.ReadAllLines(Generate(2_000_000), Encoding.UTF8);

        Assert.All(lines, l => Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} \[[A-Z ]{5}\] ", l));
        Assert.All(lines, l => Assert.InRange(Encoding.UTF8.GetByteCount(l) + 2, 1, LogFileGenerator.MaxLineBytes));
        Assert.Contains(lines, l => l.Contains("[ERROR]"));
        Assert.Contains(lines, l => l.Contains(LogFileGenerator.RareWord));
    }
}
