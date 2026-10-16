using System.Text;

namespace Lignum.Benchmarks;

/// <summary>
/// Writes realistic, deterministic UTF-8 log files of a given size: timestamped lines with a level,
/// thread, component and a message of varying length (roughly 60-250 bytes, CRLF or LF endings).
/// About 1 line in 50 is an ERROR, and about 1 in 1000 contains the word <see cref="RareWord"/>, so
/// searches have something to find.
/// </summary>
internal static class LogFileGenerator
{
    /// <summary>Upper bound for one generated line including its newline.</summary>
    public const int MaxLineBytes = 400;

    /// <summary>A word that appears on about 1 line in 1000.</summary>
    public const string RareWord = "deadlock";

    private static readonly string[] Levels = ["DEBUG", "INFO ", "INFO ", "INFO ", "WARN "];
    private static readonly string[] Components =
        ["OrderService", "HttpClient", "DbPool", "Scheduler", "AuthHandler", "CacheManager", "Kestrel", "Worker"];
    private static readonly string[] Messages =
    [
        "Request completed in {0} ms",
        "Processing order {0} for customer {1}",
        "Connection {0} returned to pool (active={1})",
        "Job {0} scheduled at offset {1}",
        "User {0} authenticated via token {1}",
        "Cache miss for key session:{0}:{1}, loading from store",
        "GET /api/v2/items/{0}?page={1} responded 200",
        "Heartbeat {0} ok, queue depth {1}",
    ];

    /// <summary>Writes at least <paramref name="targetBytes"/> bytes of whole lines to <paramref name="path"/>.</summary>
    public static void Generate(string path, long targetBytes, int seed = 1)
    {
        var random = new Random(seed);
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var builder = new StringBuilder(MaxLineBytes);
        var bytes = new byte[MaxLineBytes * 4];

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
        long written = 0;
        for (long line = 0; written < targetBytes; line++)
        {
            time = time.AddMilliseconds(random.Next(0, 50));
            BuildLine(builder, random, time, line);
            var count = Encoding.UTF8.GetBytes(builder.ToString(), bytes);
            stream.Write(bytes, 0, count);
            written += count;
        }
    }

    private static void BuildLine(StringBuilder b, Random random, DateTime time, long line)
    {
        b.Clear();
        b.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        var error = random.Next(50) == 0;
        b.Append(" [").Append(error ? "ERROR" : Levels[random.Next(Levels.Length)]).Append("] ");
        b.Append("[thread-").Append(random.Next(1, 33)).Append("] ");
        b.Append(Components[random.Next(Components.Length)]).Append(": ");
        b.AppendFormat(Messages[random.Next(Messages.Length)], random.Next(100_000), random.Next(1000));
        if (error) b.Append(" failed: System.TimeoutException: The operation has timed out");
        if (random.Next(1000) == 0) b.Append(" (possible ").Append(RareWord).Append(" detected)");

        // Pad some lines with key=value context so line lengths vary.
        var extras = random.Next(0, 6);
        for (var i = 0; i < extras; i++)
            b.Append(" ctx").Append(i).Append('=').Append(random.Next(1_000_000).ToString("x"));
        b.Append(line % 7 == 0 ? "\r\n" : "\n");
    }
}
