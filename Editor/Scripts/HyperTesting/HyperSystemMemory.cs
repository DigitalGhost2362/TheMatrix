#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Feeder.MCP.Editor.HyperTesting
{
    public readonly struct SystemMemory
    {
        public static readonly SystemMemory Unknown = new SystemMemory(-1, -1, -1, -1, -1);

        public SystemMemory(long totalMb, long availableMb, int loadPercent, long commitChargeMb, long commitLimitMb)
        {
            TotalMb = totalMb;
            AvailableMb = availableMb;
            LoadPercent = loadPercent;
            CommitChargeMb = commitChargeMb;
            CommitLimitMb = commitLimitMb;
        }

        public long TotalMb { get; }
        public long AvailableMb { get; }
        public int LoadPercent { get; }
        public long CommitChargeMb { get; }
        public long CommitLimitMb { get; }

        public bool PhysicalKnown => AvailableMb >= 0;
        public bool CommitKnown => CommitLimitMb > 0 && CommitChargeMb >= 0;
        public long CommitFreeMb => CommitKnown ? CommitLimitMb - CommitChargeMb : -1;
    }

    // one editor plus the asset import workers it started: workers are separate processes with their own commit
    public readonly struct EditorMemory
    {
        public EditorMemory(HyperProcess.Memory editor, int workers, long workersWorkingSetMb, long workersCommitMb)
        {
            Editor = editor;
            Workers = workers;
            WorkersWorkingSetMb = workersWorkingSetMb;
            WorkersCommitMb = workersCommitMb;
        }

        public HyperProcess.Memory Editor { get; }
        public int Workers { get; }
        public long WorkersWorkingSetMb { get; }
        public long WorkersCommitMb { get; }

        public long TotalWorkingSetMb => Editor.WorkingSetMb + WorkersWorkingSetMb;
        public long TotalCommitMb => Editor.CommitMb + WorkersCommitMb;
    }

    public static class HyperSystemMemory
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatusEx
        {
            public uint length;
            public uint memoryLoad;
            public ulong totalPhys;
            public ulong availPhys;
            public ulong totalPageFile;
            public ulong availPageFile;
            public ulong totalVirtual;
            public ulong availVirtual;
            public ulong availExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PerformanceInformation
        {
            public uint size;
            public UIntPtr commitTotal;
            public UIntPtr commitLimit;
            public UIntPtr commitPeak;
            public UIntPtr physicalTotal;
            public UIntPtr physicalAvailable;
            public UIntPtr systemCache;
            public UIntPtr kernelTotal;
            public UIntPtr kernelPaged;
            public UIntPtr kernelNonPaged;
            public UIntPtr pageSize;
            public uint handleCount;
            public uint processCount;
            public uint threadCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [DllImport("psapi.dll", SetLastError = true)]
        static extern bool GetPerformanceInfo(out PerformanceInformation info, uint size);

        // the crashes were commit-limit failures, so the commit charge and limit travel with the physical numbers
        public static SystemMemory Read()
        {
            try
            {
                var status = new MemoryStatusEx { length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (!GlobalMemoryStatusEx(ref status))
                    return SystemMemory.Unknown;

                long commitCharge = -1, commitLimit = -1;
                if (GetPerformanceInfo(out var info, (uint)Marshal.SizeOf<PerformanceInformation>()))
                {
                    var page = info.pageSize.ToUInt64();
                    commitCharge = (long)(info.commitTotal.ToUInt64() * page / 1048576UL);
                    commitLimit = (long)(info.commitLimit.ToUInt64() * page / 1048576UL);
                }
                return new SystemMemory((long)(status.totalPhys / 1048576), (long)(status.availPhys / 1048576), (int)status.memoryLoad, commitCharge, commitLimit);
            }
            catch (Exception)
            {
                return SystemMemory.Unknown;
            }
        }

        public static EditorMemory ReadEditor(int editorPid, IReadOnlyList<HyperProcess.Entry> snapshot)
        {
            var editor = HyperProcess.ReadMemory(editorPid);
            var workers = HyperProcess.ImportWorkers(editorPid, snapshot).Select(HyperProcess.ReadMemory).ToList();
            return new EditorMemory(editor, workers.Count, workers.Sum(w => w.WorkingSetMb), workers.Sum(w => w.CommitMb));
        }
    }
}
