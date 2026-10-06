#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Feeder.MCP.Editor.HyperTesting
{
    // Mono's System.Diagnostics.Process reads 0 for every memory property and its ProcessName throws on a process that
    // has just started, so liveness, memory, child processes and command lines come from Win32 (Windows editor only)
    public static class HyperProcess
    {
        public const string EditorExe = "Unity.exe";

        public readonly struct Entry
        {
            public Entry(int pid, int parentPid, string exe)
            {
                Pid = pid;
                ParentPid = parentPid;
                Exe = exe;
            }

            public int Pid { get; }
            public int ParentPid { get; }
            public string Exe { get; }
        }

        public readonly struct Memory
        {
            public static readonly Memory None = new Memory(0, 0, 0);

            public Memory(long workingSetMb, long peakWorkingSetMb, long commitMb)
            {
                WorkingSetMb = workingSetMb;
                PeakWorkingSetMb = peakWorkingSetMb;
                CommitMb = commitMb;
            }

            public long WorkingSetMb { get; }
            public long PeakWorkingSetMb { get; }
            public long CommitMb { get; }
        }

        const uint QueryLimitedInformation = 0x1000;
        const uint QueryInformation = 0x0400;
        const uint VmRead = 0x0010;
        const uint StillActive = 259;
        const uint SnapProcess = 2;
        const int ErrorAccessDenied = 5;
        const int PebProcessParametersOffset = 0x20;
        const int ParametersCommandLineOffset = 0x70;

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessMemoryCountersEx
        {
            public uint size;
            public uint pageFaultCount;
            public UIntPtr peakWorkingSetSize;
            public UIntPtr workingSetSize;
            public UIntPtr quotaPeakPagedPoolUsage;
            public UIntPtr quotaPagedPoolUsage;
            public UIntPtr quotaPeakNonPagedPoolUsage;
            public UIntPtr quotaNonPagedPoolUsage;
            public UIntPtr pagefileUsage;
            public UIntPtr peakPagefileUsage;
            public UIntPtr privateUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessBasicInformation
        {
            public IntPtr exitStatus;
            public IntPtr pebBaseAddress;
            public IntPtr affinityMask;
            public IntPtr basePriority;
            public IntPtr uniqueProcessId;
            public IntPtr parentProcessId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ProcessEntry32
        {
            public uint size;
            public uint usage;
            public uint processId;
            public UIntPtr defaultHeapId;
            public uint moduleId;
            public uint threads;
            public uint parentProcessId;
            public int priorityClassBase;
            public uint flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string exeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inheritHandle, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll")]
        static extern int GetCurrentProcessId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("psapi.dll", SetLastError = true)]
        static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCountersEx counters, uint size);

        [DllImport("ntdll.dll")]
        static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

        public static int CurrentPid => GetCurrentProcessId();

        public static bool IsAlive(int pid)
        {
            if (pid <= 0)
                return false;
            try
            {
                var handle = OpenProcess(QueryLimitedInformation, false, pid);
                if (handle == IntPtr.Zero)
                    return Marshal.GetLastWin32Error() == ErrorAccessDenied;
                try
                {
                    return GetExitCodeProcess(handle, out var code) && code == StillActive;
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string? ImagePath(int pid)
        {
            var handle = Open(pid, QueryLimitedInformation);
            if (handle == IntPtr.Zero)
                return null;
            try
            {
                var name = new StringBuilder(1024);
                var size = (uint)name.Capacity;
                return QueryFullProcessImageNameW(handle, 0, name, ref size) ? name.ToString() : null;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static Memory ReadMemory(int pid)
        {
            var handle = Open(pid, QueryLimitedInformation);
            if (handle == IntPtr.Zero)
                handle = Open(pid, QueryInformation | VmRead);
            if (handle == IntPtr.Zero)
                return Memory.None;
            try
            {
                return GetProcessMemoryInfo(handle, out var counters, (uint)Marshal.SizeOf<ProcessMemoryCountersEx>())
                    ? new Memory(ToMb(counters.workingSetSize), ToMb(counters.peakWorkingSetSize), ToMb(counters.privateUsage))
                    : Memory.None;
            }
            catch (Exception)
            {
                return Memory.None;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static List<Entry> Snapshot()
        {
            var entries = new List<Entry>();
            try
            {
                var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
                if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                    return entries;
                try
                {
                    var entry = new ProcessEntry32 { size = (uint)Marshal.SizeOf<ProcessEntry32>() };
                    for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                        entries.Add(new Entry((int)entry.processId, (int)entry.parentProcessId, entry.exeFile ?? string.Empty));
                }
                finally
                {
                    CloseHandle(snapshot);
                }
            }
            catch (Exception)
            {
            }
            return entries;
        }

        public static bool IsEditorExe(string exe) => string.Equals(exe, EditorExe, StringComparison.OrdinalIgnoreCase);

        // asset import workers are Unity.exe children started with "-name AssetImportWorkerN"
        public static List<int> ImportWorkers(int editorPid, IReadOnlyList<Entry> snapshot) =>
            snapshot.Where(e => e.ParentPid == editorPid && IsEditorExe(e.Exe) && IsImportWorker(CommandLine(e.Pid))).Select(e => e.Pid).ToList();

        // the live editor whose -projectPath is the given folder, 0 when none runs
        public static int FindEditor(string projectRoot, IReadOnlyList<Entry> snapshot)
        {
            var wanted = NormalizePath(projectRoot);
            foreach (var entry in snapshot.Where(e => IsEditorExe(e.Exe)))
            {
                var commandLine = CommandLine(entry.Pid);
                if (commandLine != null && !IsImportWorker(commandLine) && NormalizePath(ProjectPathOf(commandLine)) == wanted)
                    return entry.Pid;
            }
            return 0;
        }

        // true for a live Unity.exe that was started on this folder; an unreadable command line falls back to the exe name alone
        public static bool IsEditorOf(int pid, string projectRoot)
        {
            if (!IsAlive(pid))
                return false;
            var image = ImagePath(pid);
            if (image == null || !IsEditorExe(Path.GetFileName(image)))
                return false;
            var commandLine = CommandLine(pid);
            return commandLine == null || NormalizePath(ProjectPathOf(commandLine)) == NormalizePath(projectRoot);
        }

        public static string? CommandLine(int pid)
        {
            if (IntPtr.Size != 8)
                return null;
            var handle = Open(pid, QueryInformation | VmRead);
            if (handle == IntPtr.Zero)
                return null;
            try
            {
                var info = new ProcessBasicInformation();
                if (NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0 || info.pebBaseAddress == IntPtr.Zero)
                    return null;
                var parameters = ReadBytes(handle, info.pebBaseAddress.ToInt64() + PebProcessParametersOffset, 8);
                if (parameters == null)
                    return null;
                var text = ReadBytes(handle, BitConverter.ToInt64(parameters, 0) + ParametersCommandLineOffset, 16);
                if (text == null)
                    return null;
                int length = BitConverter.ToUInt16(text, 0);
                var buffer = BitConverter.ToInt64(text, 8);
                if (length == 0 || buffer == 0)
                    return null;
                var chars = ReadBytes(handle, buffer, length);
                return chars == null ? null : Encoding.Unicode.GetString(chars);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        internal static bool IsImportWorker(string? commandLine)
        {
            if (commandLine == null)
                return false;
            var args = SplitCommandLine(commandLine);
            for (var i = 0; i + 1 < args.Count; i++)
            {
                if (string.Equals(args[i], "-name", StringComparison.OrdinalIgnoreCase) && args[i + 1].StartsWith("AssetImport", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static string ProjectPathOf(string commandLine)
        {
            var args = SplitCommandLine(commandLine);
            for (var i = 0; i + 1 < args.Count; i++)
            {
                if (string.Equals(args[i], "-projectPath", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return string.Empty;
        }

        internal static List<string> SplitCommandLine(string commandLine)
        {
            var args = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            var started = false;
            foreach (var ch in commandLine)
            {
                if (ch == '"')
                {
                    quoted = !quoted;
                    started = true;
                }
                else if (char.IsWhiteSpace(ch) && !quoted)
                {
                    if (started)
                        args.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
                else
                {
                    current.Append(ch);
                    started = true;
                }
            }
            if (started)
                args.Add(current.ToString());
            return args;
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            try
            {
                return Path.GetFullPath(path).Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
            }
            catch (Exception)
            {
                return path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
            }
        }

        static IntPtr Open(int pid, uint access)
        {
            if (pid <= 0)
                return IntPtr.Zero;
            try
            {
                return OpenProcess(access, false, pid);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        static byte[]? ReadBytes(IntPtr handle, long address, int length)
        {
            var buffer = new byte[length];
            return ReadProcessMemory(handle, new IntPtr(address), buffer, new IntPtr(length), out var read) && read.ToInt64() == length ? buffer : null;
        }

        static long ToMb(UIntPtr bytes) => (long)(bytes.ToUInt64() / 1048576UL);
    }
}
