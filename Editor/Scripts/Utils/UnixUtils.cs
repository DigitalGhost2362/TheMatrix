#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Feeder.MCP.Editor.Utils
{
    public static class UnixUtils
    {
        // 0755 in octal (rwxr-xr-x)
        private const uint MODE_0755 = 0x1ED;

        /// <summary>
        /// Sets the file permission to 0755 (rwxr-xr-x) on macOS and Linux.
        /// No-ops on Windows. Throws if the file is missing or chmod fails.
        /// </summary>
        public static void Set0755(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is null or empty.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("File not found.", path);

            if (!IsUnixLike) return; // Windows: nothing to do

            // Ensure absolute path to avoid surprises
            var full = Path.GetFullPath(path);

            // P/Invoke chmod (works on both macOS and Linux)
            int rc = chmod(full, MODE_0755);
            if (rc != 0)
            {
                int errno = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"chmod(0755) failed (errno {errno}) for '{full}'.");
            }
        }

        private static bool IsUnixLike =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        // libc is correct on both macOS and Linux for chmod(2)
        [DllImport("libc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int chmod(string pathname, uint mode);
    }
}