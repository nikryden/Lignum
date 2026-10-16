using System.Diagnostics;
using Lignum.Core;

namespace Lignum.Benchmarks;

/// <summary>Measures the spec's targets through the public <see cref="LogDocument"/> API only.</summary>
internal static class LogDocumentBenchmarks
{
    private const int ScreenRows = 60;
    private const int ScreenColumns = 1000;

    public static async Task RunAsync(string path)
    {
        var length = new FileInfo(path).Length;
        Console.WriteLine($"File: {path} ({Format.Bytes(length)})");
        Console.WriteLine("Note: a freshly generated file is in the OS file cache, so these are warm-cache numbers.");
        Console.WriteLine();

        using var document = LogDocument.Open(path);

        // First screen: open, start indexing, wait for one screen of lines, read it.
        var total = Stopwatch.StartNew();
        var screenReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        document.LinesAdded += (_, e) =>
        {
            if (e.LineCount >= ScreenRows) screenReady.TrySetResult();
        };
        var indexing = document.StartIndexing();
        await Task.WhenAny(screenReady.Task, indexing);
        var prefixes = document.ReadLinePrefixes(0, (int)Math.Min(ScreenRows, document.LineCount), ScreenColumns);
        var firstScreen = total.Elapsed;

        await indexing;
        var indexed = total.Elapsed;
        if (document.IndexingState != IndexingState.Completed)
            throw new InvalidOperationException($"Indexing ended {document.IndexingState}: {document.IndexingError}");

        var managedAfterIndex = GC.GetTotalMemory(forceFullCollection: true);
        var peakAfterIndex = Process.GetCurrentProcess().PeakWorkingSet64;

        var rows = new List<(string Name, TimeSpan Time, string Target, string Detail)>
        {
            ("First screen", firstScreen, "< 1 s", $"{prefixes.Length} lines"),
            ("Full index", indexed, "~5-10 s", $"{document.LineCount:N0} lines, {Throughput(length, indexed)}"),
        };

        rows.Add(await SearchAsync(document, "Plain search (ignore case)", Query.Find(LogFileGenerator.RareWord), length));
        rows.Add(await SearchAsync(document, "Plain search (match case)", Query.Find("TimeoutException", matchCase: true), length));
        const string pattern = @"\[ERROR\] \[thread-\d+\] \w+: .*timed out";
        rows.Add(await SearchAsync(document, "Regex search", Query.Find(pattern, isRegex: true), length));
        rows.Add(await SearchAsync(document, "Regex search (NonBacktracking)",
            Query.Find(pattern, isRegex: true, nonBacktracking: true), length));

        Console.WriteLine($"{"Measure",-32} {"Time",10}  {"Target",-9} Detail");
        foreach (var (name, time, target, detail) in rows)
            Console.WriteLine($"{name,-32} {Format.Seconds(time),10}  {target,-9} {detail}");

        var peak = Process.GetCurrentProcess().PeakWorkingSet64;
        Console.WriteLine();
        Console.WriteLine($"Managed heap after indexing: {Format.Bytes(managedAfterIndex)} " +
                          $"({(double)managedAfterIndex / Math.Max(1, document.LineCount):0.0} bytes/line)");
        Console.WriteLine($"Peak working set: {Format.Bytes(peakAfterIndex)} after indexing, {Format.Bytes(peak)} overall");
    }

    private static async Task<(string, TimeSpan, string, string)> SearchAsync(
        LogDocument document, string name, Query query, long length)
    {
        var sw = Stopwatch.StartNew();
        using var result = document.StartQuery(query);
        await result.Completion;
        var elapsed = sw.Elapsed;
        if (result.State != QueryState.Completed)
            throw new InvalidOperationException($"{name} ended {result.State}: {result.Error}");
        return (name, elapsed, "~10-20 s", $"{result.Count:N0} matches, {Throughput(length, elapsed)}");
    }

    private static string Throughput(long bytes, TimeSpan time) =>
        $"{bytes / (double)(1 << 20) / Math.Max(time.TotalSeconds, 1e-9):0} MB/s";
}
