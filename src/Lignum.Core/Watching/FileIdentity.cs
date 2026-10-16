using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lignum.Core.Watching;

/// <summary>
/// Which file a handle refers to (volume serial plus file index on Windows). Creation time is not good
/// enough: NTFS "tunnelling" gives a file created right after another was deleted or renamed away the
/// old one's creation time, which is exactly what log rotation does.
/// </summary>
internal readonly record struct FileIdentity(uint Volume, ulong Index)
{
    /// <summary>The identity of the open file, or null where it cannot be determined (non-Windows).</summary>
    public static FileIdentity? Of(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!GetFileInformationByHandle(handle, out var info)) return null;
        return new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)] // FILETIMEs are two DWORDs, 4-byte aligned
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

#pragma warning disable SYSLIB1054 // LibraryImport needs AllowUnsafeBlocks; this one blittable call does not justify it.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation info);
#pragma warning restore SYSLIB1054
}
