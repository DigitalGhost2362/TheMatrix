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
        Foreign,
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

    // a server is identified by the exe it runs from: <project>/Library/feeder-matrix/<rid>/..., never by its process name
    public static class ServerOwnership
    {
        // every name the server lifecycle owns, all under the project's own Library and Temp folders
        public const string ExeName = "matrix-server";
        public const string FolderName = "feeder-matrix"; // Library/feeder-matrix/<rid>/ and Temp/feeder-matrix/
        public const string StagingPrefix = FolderName + "-staging-"; // Library/feeder-matrix-staging-<guid>
        public const string OwnerFileName = "server-owner.json"; // Temp/feeder-matrix/server-owner.json
        public const int MinAutoPort = 20000;
        public const int MaxAutoPort = 29999;

        static StringComparison PathComparison => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static string OwnerFilePath
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", FolderName, OwnerFileName));

        #region Classify

        // ours = under our root; OtherProject = under another project's Library/feeder-matrix; anything else is foreign
        public static ServerOwner Classify(string? exePath, string ownRoot)
        {
            if (string.IsNullOrEmpty(exePath))
                return ServerOwner.Unknown;

            var root = ownRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full;
            try { full = Path.GetFullPath(exePath); }
            catch (Exception) { return ServerOwner.Unknown; }
            if (full.StartsWith(root, PathComparison))
                return ServerOwner.Ours;
            var marker = $"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}{FolderName}{Path.DirectorySeparatorChar}";
            return full.IndexOf(marker, PathComparison) > 0 ? ServerOwner.OtherProject : ServerOwner.Foreign;
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
            try
            {
                if (process.HasExited)
                    return ServerOwner.Unknown;
            }
            catch (Exception)
            {
                return ServerOwner.Unknown;
            }
            return Classify(ReadExePath(process), ownRoot);
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

        // "<P>/Library/feeder-matrix/<rid>/matrix-server.exe" -> "P"
        public static string DescribeOwner(string? exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return "another process";
            var marker = $"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}{FolderName}";
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

        #region Legacy Install

        // Before 0.88 the server lived in <project>/Library/mcp-server/<rid>/, a folder name other tools may use as well. Such
        // a folder is ours only when every file in it is one our installer wrote (our marker, web.config, version, and
        // executables whose PE identity is our server); anything else in it and the whole folder is left alone.
        public const string LegacyFolderName = "mcp-server";
        public const string LegacyMarkerFileName = "feeder-mcp-server.staticwebassets.endpoints.json";
        public const string LegacyExeIdentity = "feeder-mcp-server.dll";
        static readonly string[] LegacyPayloadFiles = { "web.config", "version" };

        internal static Func<string, string> ExeIdentity = file =>
        {
            try { return FileVersionInfo.GetVersionInfo(file).OriginalFilename ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        };

        public static bool IsLegacyInstall(string folder)
            => File.Exists(Path.Combine(folder, LegacyMarkerFileName))
               && Directory.GetDirectories(folder).Length == 0
               && Directory.GetFiles(folder).All(IsLegacyPayloadFile);

        static bool IsLegacyPayloadFile(string file)
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, LegacyMarkerFileName, StringComparison.OrdinalIgnoreCase)
                || LegacyPayloadFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                return true;
            return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(ExeIdentity(file), LegacyExeIdentity, StringComparison.OrdinalIgnoreCase);
        }

        // one line per action; a folder that is not ours is left byte-for-byte alone
        public static List<string> RemoveLegacyInstall(string folder)
        {
            var actions = new List<string>();
            if (!Directory.Exists(folder) || !IsLegacyInstall(folder))
                return actions;

            var exes = Directory.GetFiles(folder).Where(file => file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var exe in exes)
                foreach (var pid in KillProcessesRunning(exe))
                    actions.Add($"stopped PID {pid} running {exe}");

            // the marker goes last, so a still locked exe keeps the folder recognisable for the next editor start
            var files = exes.Concat(LegacyPayloadFiles.Select(name => Path.Combine(folder, name))).Append(Path.Combine(folder, LegacyMarkerFileName));
            foreach (var file in files.Where(File.Exists))
            {
                try
                {
                    File.Delete(file);
                    actions.Add($"deleted {file}");
                }
                catch (Exception ex)
                {
                    actions.Add($"could not delete {file}, retrying on the next editor start: {ex.Message}");
                    return actions;
                }
            }

            if (Directory.GetFileSystemEntries(folder).Length == 0)
            {
                Directory.Delete(folder);
                actions.Add($"deleted the empty folder {folder}");
            }
            return actions;
        }

        // kills only processes whose executable is exactly this file; the name lookup merely narrows the candidates
        public static List<int> KillProcessesRunning(string exePath)
        {
            var killed = new List<int>();
            var target = Path.GetFullPath(exePath);
            Process[] candidates;
            try { candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target)); }
            catch (Exception) { return killed; }

            foreach (var process in candidates)
            {
                try
                {
                    var path = ReadExePath(process);
                    if (path == null || process.HasExited || !string.Equals(Path.GetFullPath(path), target, PathComparison))
                        continue;
                    process.Kill();
                    process.WaitForExit(5000);
                    killed.Add(process.Id);
                }
                catch (Exception)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return killed;
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
