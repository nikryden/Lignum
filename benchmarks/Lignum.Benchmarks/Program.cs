using System.Diagnostics;
using System.Globalization;
using Lignum.Benchmarks;
using Lignum.Core;

// Manual-only benchmarks for the spec's performance targets (2 GB file on an SSD):
// first screen < 1 s, full index ~5-10 s, full regex search ~10-20 s.
//
//   dotnet run -c Release --project benchmarks/Lignum.Benchmarks -- [--size 2GB] [--dir <folder>] [--file <log>] [--keep]
//
// --size  size of the generated file (KB/MB/GB suffixes; default 2GB)
// --dir   where generated files go (default %TEMP%\lignum-bench); an existing file of that size is reused
// --file  benchmark an existing file instead of generating one
// --keep  keep the generated file afterwards (default: delete it)

var options = BenchmarkOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("usage: Lignum.Benchmarks [--size 2GB] [--dir <folder>] [--file <log>] [--keep]");
    return 2;
}

var path = options.File;
var generated = false;
if (path is null)
{
    Directory.CreateDirectory(options.Directory);
    path = Path.Combine(options.Directory, $"bench-{options.Size}.log");
    if (!File.Exists(path) || new FileInfo(path).Length < options.Size)
    {
        Console.WriteLine($"Generating {Format.Bytes(options.Size)} into {path} ...");
        var sw = Stopwatch.StartNew();
        LogFileGenerator.Generate(path, options.Size);
        Console.WriteLine($"  generated in {Format.Seconds(sw.Elapsed)}");
        generated = true;
    }
}

try
{
    await LogDocumentBenchmarks.RunAsync(path);
}
finally
{
    if (generated && !options.Keep) File.Delete(path);
}

return 0;

internal sealed record BenchmarkOptions(long Size, string Directory, string? File, bool Keep)
{
    public static BenchmarkOptions? Parse(string[] args)
    {
        long size = 2L << 30;
        var dir = Path.Combine(Path.GetTempPath(), "lignum-bench");
        string? file = null;
        var keep = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--size" when i + 1 < args.Length:
                    if (!Format.TryParseBytes(args[++i], out size)) return null;
                    break;
                case "--dir" when i + 1 < args.Length:
                    dir = args[++i];
                    break;
                case "--file" when i + 1 < args.Length:
                    file = args[++i];
                    break;
                case "--keep":
                    keep = true;
                    break;
                default:
                    return null;
            }
        }

        return new BenchmarkOptions(size, dir, file, keep);
    }
}

internal static class Format
{
    public static string Seconds(TimeSpan t) => $"{t.TotalSeconds:0.000} s";

    public static string Bytes(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.00} GB" : $"{b / (double)(1L << 20):0.0} MB";

    public static bool TryParseBytes(string text, out long bytes)
    {
        var t = text.Trim().ToUpperInvariant();
        long unit = 1;
        foreach (var (suffix, value) in new[] { ("GB", 1L << 30), ("MB", 1L << 20), ("KB", 1L << 10) })
        {
            if (t.EndsWith(suffix, StringComparison.Ordinal))
            {
                unit = value;
                t = t[..^2];
                break;
            }
        }

        var ok = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number > 0;
        bytes = ok ? (long)(number * unit) : 0;
        return ok;
    }
}
