namespace Lignum.Core.Searching;

/// <summary>Finds the files of a folder to search (ticket 02) and tells binary files apart from text.</summary>
public static class FileFinder
{
    /// <summary>How much of a file <see cref="LooksBinary"/> reads.</summary>
    public const int BinarySampleSize = 8192;

    /// <summary>
    /// The files in <paramref name="folder"/> (and its subfolders if <paramref name="includeSubfolders"/>) that
    /// match any of <paramref name="patterns"/> (e.g. <c>*.log;*.txt</c>; empty means every file), sorted by
    /// path, with their total size. Folders that cannot be read are skipped.
    /// </summary>
    public static (List<string> Files, long TotalBytes) Find(string folder, bool includeSubfolders, string patterns,
                                                             CancellationToken cancellationToken = default)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };
        var masks = patterns.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (masks.Length == 0) masks = ["*"];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        long total = 0;
        var root = new DirectoryInfo(folder);
        foreach (var mask in masks)
        {
            foreach (var file in root.EnumerateFiles(mask, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(file.FullName)) continue;
                files.Add(file.FullName);
                total += file.Length;
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return (files, total);
    }

    /// <summary>
    /// The text files directly in <paramref name="folder"/> (not its subfolders), sorted by path, for opening a
    /// folder. Binary files (<see cref="LooksBinary"/>) are left out and counted, as are files that cannot be read.
    /// Throws when the folder itself cannot be listed.
    /// </summary>
    public static (List<string> Files, int Binary, int Unreadable) FindTextFiles(string folder,
                                                                                 CancellationToken cancellationToken = default)
    {
        var (all, _) = Find(folder, includeSubfolders: false, patterns: "", cancellationToken);
        var files = new List<string>(all.Count);
        int binary = 0, unreadable = 0;
        foreach (var file in all)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (LooksBinary(file)) binary++;
                else files.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable++;
            }
        }

        return (files, binary, unreadable);
    }

    /// <summary>
    /// True when the start of the file has a null byte (and no UTF-16 byte order mark): an executable, image or
    /// archive rather than a log. Reads at most <see cref="BinarySampleSize"/> bytes, without locking the file.
    /// UTF-16 text without a byte order mark also counts as binary.
    /// </summary>
    public static bool LooksBinary(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1);
        Span<byte> sample = stackalloc byte[BinarySampleSize];
        var bytes = sample[..stream.ReadAtLeast(sample, sample.Length, throwOnEndOfStream: false)];
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return false;
        return bytes.IndexOf((byte)0) >= 0;
    }
}
