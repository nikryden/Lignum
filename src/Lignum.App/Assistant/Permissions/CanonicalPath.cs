using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Lignum.App.Assistant.Permissions;

/// <summary>
/// The one spelling of a path that permission checks compare (ADR 0002): full, with <c>..</c> resolved, and with
/// symlinks and junctions resolved to their final target. A path that does not exist yet (a file to write) is
/// resolved through its deepest existing folder. Comparison is case-insensitive, as Windows paths are.
/// </summary>
public static class CanonicalPath
{
    public static string Of(string path)
    {
        var full = Path.GetFullPath(path);
        var existing = full;
        var rest = new Stack<string>();
        while (!File.Exists(existing) && !Directory.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent is null) return TrimEnd(full); // nothing of it exists, not even the drive
            rest.Push(Path.GetFileName(existing));
            existing = parent;
        }

        var resolved = FinalPath(existing) ?? existing;
        while (rest.Count > 0) resolved = Path.Combine(resolved, rest.Pop());
        return TrimEnd(resolved);
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it (both canonical).</summary>
    public static bool IsUnder(string path, string folder)
    {
        if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Drops a trailing separator, except on a root such as <c>C:\</c>.</summary>
    private static string TrimEnd(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 || trimmed.EndsWith(':') ? trimmed + Path.DirectorySeparatorChar : trimmed;
    }

    /// <summary>The final path of an existing file or folder, every link resolved; null when Windows cannot tell.</summary>
    private static string? FinalPath(string existing)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // No access rights asked for: this only reads the name, and never locks the file (a log may be open in a tab).
        using var handle = CreateFile(existing, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open,
            BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) return null;
        var final = buffer.ToString();
        if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + final[8..];
        return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    private const uint BackupSemantics = 0x02000000; // FILE_FLAG_BACKUP_SEMANTICS: lets CreateFile open folders

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security,
        FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
}
