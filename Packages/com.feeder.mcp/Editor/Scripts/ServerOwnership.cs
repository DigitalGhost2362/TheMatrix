#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Feeder.MCP.Editor
{
    public enum ServerOwner
    {
        Ours,
        OtherProject,
        NotMcp,
        Unknown,
    }

    [Serializable]
    public class ServerOwnerRecord
    {
        public int pid;
        public long startTimeUtcTicks;
        public int port;
        public string exePath = string.Empty;
    }

    // a server is identified by the exe it runs from: <project>/Library/mcp-server/<rid>/..., true for every plugin version
    public static class ServerOwnership
    {
        public const string ProcessName = "matrix-server";
        // packages up to 0.87 shipped the exe under this name; still recognised so an update can stop and replace it
        public const string LegacyProcessName = "gamedev-mcp-server";
        public static readonly string[] AllProcessNames = { ProcessName, LegacyProcessName };
        public const int MinAutoPort = 20000;
        public const int MaxAutoPort = 29999;

        static StringComparison PathComparison => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static string OwnerFilePath
            => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp", "mcp-server-owner.json"));

        #region Classify

        public static ServerOwner Classify(string? processName, string? exePath, string ownRoot)
        {
            if (processName == null)
                return ServerOwner.Unknown;
            if (!AllProcessNames.Any(name => processName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                return ServerOwner.NotMcp;
            if (string.IsNullOrEmpty(exePath))
                return ServerOwner.Unknown;

            var root = ownRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full;
            try { full = Path.GetFullPath(exePath); }
            catch (Exception) { return ServerOwner.Unknown; }
            return full.StartsWith(root, PathComparison) ? ServerOwner.Ours : ServerOwner.OtherProject;
        }

        public static ServerOwner ClassifyProcess(int pid, string ownRoot)
        {
            if (pid <= 0)
                return ServerOwner.Unknown;
            try
            {
                using var process = Process.GetProcessById(pid);
                return ClassifyProcess(process, ownRoot);
            }
            catch (Exception)
            {
                return ServerOwner.Unknown;
            }
        }

        public static ServerOwner ClassifyProcess(Process process, string ownRoot)
        {
            string? name;
            try
            {
                if (process.HasExited)
                    return ServerOwner.Unknown;
                name = process.ProcessName;
            }
            catch (Exception)
            {
                return ServerOwner.Unknown;
            }
            return Classify(name, ReadExePath(process), ownRoot);
        }

        public static string? ReadExePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // "<P>/Library/mcp-server/<rid>/matrix-server.exe" -> "P"
        public static string DescribeOwner(string? exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return "another process";
            var marker = $"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}mcp-server";
            var full = exePath!.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var index = full.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index > 0 ? Path.GetFileName(full.Substring(0, index)) : Path.GetFileName(full);
        }

        #endregion

        #region Owner File

        public static bool CanAdopt(ServerOwnerRecord record, DateTime? actualStartUtc, ServerOwner owner, int currentPort)
            => record.pid > 0
               && actualStartUtc.HasValue
               && actualStartUtc.Value.Ticks == record.startTimeUtcTicks
               && owner == ServerOwner.Ours
               && record.port == currentPort;

        public static ServerOwnerRecord? ParseRecord(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var record = JsonUtility.FromJson<ServerOwnerRecord>(json);
                return record != null && record.pid > 0 ? record : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ServerOwnerRecord? ReadRecord()
        {
            try
            {
                var path = OwnerFilePath;
                return File.Exists(path) ? ParseRecord(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void WriteRecord(ServerOwnerRecord record)
        {
            var path = OwnerFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonUtility.ToJson(record));
        }

        public static void DeleteRecord()
        {
            try
            {
                var path = OwnerFilePath;
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        public static DateTime? ReadStartTimeUtc(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime();
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region Ports

        public static int PickFreePort(int current, ICollection<int> busy, ICollection<int> reserved, int min = MinAutoPort, int max = MaxAutoPort)
        {
            var span = max - min + 1;
            var start = current >= min && current <= max ? current - min + 1 : 0;
            for (var offset = 0; offset < span; offset++)
            {
                var port = min + (start + offset) % span;
                if (port != current && !busy.Contains(port) && !reserved.Contains(port))
                    return port;
            }
            return -1;
        }

        public static bool IsPortListening(int port)
        {
            try
            {
                return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static HashSet<int> ListeningPorts()
        {
            try
            {
                return new HashSet<int>(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(endpoint => endpoint.Port));
            }
            catch (Exception)
            {
                return new HashSet<int>();
            }
        }

        #endregion
    }

    public sealed class ServerRestartPolicy
    {
        public static readonly TimeSpan[] Backoff =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
        };

        public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan HealthyLifetime = TimeSpan.FromMinutes(2);

        readonly List<DateTime> _restarts = new();

        public int Attempts => _restarts.Count;

        public static int MaxAttempts => Backoff.Length;

        // null = give up: the server crashed MaxAttempts times inside Window
        public TimeSpan? NextDelay(DateTime nowUtc, TimeSpan? lastLifetime)
        {
            if (lastLifetime.HasValue && lastLifetime.Value > HealthyLifetime)
                _restarts.Clear();
            _restarts.RemoveAll(time => nowUtc - time > Window);
            if (_restarts.Count >= Backoff.Length)
                return null;
            var delay = Backoff[_restarts.Count];
            _restarts.Add(nowUtc);
            return delay;
        }

        public void Reset() => _restarts.Clear();
    }
}
