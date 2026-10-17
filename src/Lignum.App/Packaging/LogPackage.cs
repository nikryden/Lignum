using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Lignum.App.Settings;
using Lignum.Core;

namespace Lignum.App.Packaging;

/// <summary><c>manifest.json</c> in a <c>.lm</c> package.</summary>
public sealed class PackageManifest
{
    public int FormatVersion { get; set; } = 1;

    /// <summary>The log's original file name; also its entry name in the zip.</summary>
    public string FileName { get; set; } = "";

    public long Size { get; set; }

    /// <summary>Lower-case hex SHA-256 of the log.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>The encoding the log was read with when it was not detected; null = detect.</summary>
    public LogEncoding? Encoding { get; set; }

    public string CreatedBy { get; set; } = "";

    public DateTimeOffset Created { get; set; }
}

/// <summary>A package extracted for viewing: the log and its <c>bookmarks.json</c> sit in <see cref="Folder"/>.</summary>
public sealed record OpenedPackage(string PackagePath, string LogPath, string Folder, PackageManifest Manifest,
                                   bool ChecksumMatches);

/// <summary>
/// A <c>.lm</c> package (ticket 06): a zip with a log (unchanged, under its own name), <c>bookmarks.json</c>
/// (the per-file bookmarks schema, with notes) and <c>manifest.json</c>. The log is copied in 1 MB blocks,
/// never loaded whole, and opened with the usual share modes so the writer is never blocked. Opening a
/// package extracts it to a temp folder, because a compressed entry cannot be read by position.
/// </summary>
public static class LogPackage
{
    public const string Extension = ".lm";
    public const string BookmarksEntry = "bookmarks.json";
    private const string ManifestEntry = "manifest.json";
    private const int BufferSize = 1 << 20;

    /// <summary><c>%TEMP%\Lignum\packages</c>: one sub-folder per opened package.</summary>
    public static string DefaultFolder { get; } = Path.Combine(Path.GetTempPath(), "Lignum", "packages");

    public static bool IsPackage(string path) =>
        string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes <paramref name="logPath"/> and <paramref name="bookmarks"/> to a package at
    /// <paramref name="destination"/>. <paramref name="compress"/> false stores the log as is (faster, for
    /// huge logs). A failed or cancelled export leaves no file behind. Runs on a background thread.
    /// </summary>
    public static Task ExportAsync(string logPath, BookmarksFile bookmarks, LogEncoding? encoding, string destination,
                                   bool compress, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Export(logPath, bookmarks, encoding, destination, compress, progress, cancellationToken),
            cancellationToken);

    private static void Export(string logPath, BookmarksFile bookmarks, LogEncoding? encoding, string destination,
                               bool compress, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var temp = destination + ".tmp";
        try
        {
            using (var zipFile = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create))
            {
                var name = Path.GetFileName(logPath);
                long size;
                string hash;
                using (var source = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan))
                using (var target = zip.CreateEntry(name,
                           compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression).Open())
                {
                    (size, hash) = Copy(source, target, source.Length, progress, cancellationToken);
                }

                WriteJson(zip, BookmarksEntry, bookmarks);
                WriteJson(zip, ManifestEntry, new PackageManifest
                {
                    FileName = name,
                    Size = size,
                    Sha256 = hash,
                    Encoding = encoding,
                    CreatedBy = "Lignum " + Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
                    Created = DateTimeOffset.Now,
                });
            }

            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Extracts <paramref name="packagePath"/> into a new folder under <paramref name="extractRoot"/> and checks
    /// the log against the manifest's SHA-256. Throws <see cref="InvalidDataException"/> for a damaged
    /// package (or a file that is not one), and the usual file exceptions. Runs on a background thread.
    /// </summary>
    public static Task<OpenedPackage> OpenAsync(string packagePath, string extractRoot, IProgress<double>? progress,
                                                CancellationToken cancellationToken) =>
        Task.Run(() => Open(packagePath, extractRoot, progress, cancellationToken), cancellationToken);

    private static OpenedPackage Open(string packagePath, string extractRoot, IProgress<double>? progress,
                                      CancellationToken cancellationToken)
    {
        using var zipFile = new FileStream(packagePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(zipFile, ZipArchiveMode.Read);
        var manifest = ReadJson<PackageManifest>(zip, ManifestEntry)
                       ?? throw new InvalidDataException("The package has no manifest.json.");
        var name = Path.GetFileName(manifest.FileName); // never a path: nothing is written outside the folder
        var logEntry = (name.Length > 0 ? zip.GetEntry(name) : null)
                       ?? throw new InvalidDataException($"The package does not contain its log \"{manifest.FileName}\".");

        var folder = Path.Combine(extractRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var logPath = Path.Combine(folder, name);
            string hash;
            using (var source = logEntry.Open())
            using (var target = new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                       BufferSize))
            {
                (_, hash) = Copy(source, target, logEntry.Length, progress, cancellationToken);
            }

            zip.GetEntry(BookmarksEntry)?.ExtractToFile(Path.Combine(folder, BookmarksEntry));
            return new OpenedPackage(Path.GetFullPath(packagePath), logPath, folder, manifest,
                string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            DeleteExtracted(folder);
            throw;
        }
    }

    /// <summary>Deletes an extracted package folder; retries briefly while the log's handle is still closing.</summary>
    public static void DeleteExtracted(string folder)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Deletes extracted folders older than <paramref name="age"/> (left by a crash). Never throws.</summary>
    public static void DeleteStaleFolders(string root, TimeSpan age)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > age) DeleteExtracted(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static (long Size, string Sha256) Copy(Stream source, Stream target, long expectedLength,
                                                   IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.Write(buffer, 0, read);
            sha.AppendData(buffer, 0, read);
            total += read;
            progress?.Report(expectedLength > 0 ? Math.Min(1, (double)total / expectedLength) : 1);
        }

        return (total, Convert.ToHexStringLower(sha.GetHashAndReset()));
    }

    private static void WriteJson<T>(ZipArchive zip, string entryName, T value)
    {
        using var stream = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
        JsonSerializer.Serialize(stream, value, SettingsStore.JsonOptions);
    }

    private static T? ReadJson<T>(ZipArchive zip, string entryName)
    {
        if (zip.GetEntry(entryName) is not { } entry) return default;
        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<T>(stream, SettingsStore.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{entryName} in the package is damaged: {ex.Message}", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
