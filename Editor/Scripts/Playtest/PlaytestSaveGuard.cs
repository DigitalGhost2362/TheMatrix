#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Playtest
{
    // a playtest clicks real buttons, and in the Editor a rewarded ad counts as watched at once: rewards, purchases and
    // new saves are real. The guard snapshots PlayerPrefs and persistentDataPath before the run, lists what the run
    // changed and puts the snapshot back once play mode has ended.
    internal static class PlaytestSaveGuard
    {
        const long BackupLimitBytes = 256L * 1024 * 1024;
        const int MaxListed = 30;

        static string Folder => Path.Combine(PlaytestRunner.ProjectRoot, "Library", "MatrixPlaytest");
        static string PrefsBefore => Path.Combine(Folder, Application.platform == RuntimePlatform.WindowsEditor ? "prefs-before.reg" : "prefs-before");
        static string FilesBefore => Path.Combine(Folder, "files-before.txt");
        static string Backup => Path.Combine(Folder, "save-backup");
        static string SaveFolder => Application.persistentDataPath;

        // play mode PlayerPrefs of the Editor; a built player uses another place
        public static string PrefsLocation => Application.platform switch
        {
            RuntimePlatform.WindowsEditor => $@"HKCU\Software\Unity\UnityEditor\{PlayerSettings.companyName}\{PlayerSettings.productName}",
            RuntimePlatform.OSXEditor => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Preferences",
                $"unity.{PlayerSettings.companyName}.{PlayerSettings.productName}.plist"),
            _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", "unity3d", PlayerSettings.companyName,
                PlayerSettings.productName, "prefs"),
        };

        public static void Snapshot()
        {
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, true);
            Directory.CreateDirectory(Folder);
            PlayerPrefs.Save();
            if (Application.platform == RuntimePlatform.WindowsEditor)
                Reg($"export \"{PrefsLocation}\" \"{PrefsBefore}\" /y");
            else if (File.Exists(PrefsLocation))
                File.Copy(PrefsLocation, PrefsBefore, true);

            var files = ListFiles();
            File.WriteAllLines(FilesBefore, files.Select(f => $"{f.Key}\t{f.Value.size}\t{f.Value.ticks}"));
            if (files.Values.Sum(f => f.size) <= BackupLimitBytes)
                foreach (var file in files.Keys)
                {
                    var to = Path.Combine(Backup, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(Path.Combine(SaveFolder, file), to, true);
                }
        }

        // the 'Side effects' section of the report; restore = put the snapshot back
        public static string Compare(bool restore)
        {
            var text = new StringBuilder("\n## Side effects\n\n");
            if (!File.Exists(FilesBefore))
                return text.Append("No snapshot was taken before the run.\n").ToString();
            PlayerPrefs.Save();
            ComparePrefs(text, restore);
            CompareFiles(text, restore);
            if (!restore)
                text.Append("\nNot restored: play mode is still on (stay=true or the run started in play mode). ")
                    .Append("Run again without stay to restore, or put the values above back by hand.\n");
            return text.ToString();
        }

        static void ComparePrefs(StringBuilder text, bool restore)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                var before = File.Exists(PrefsBefore) ? File.ReadAllBytes(PrefsBefore) : null;
                var after = File.Exists(PrefsLocation) ? File.ReadAllBytes(PrefsLocation) : null;
                var changed = before == null ? after != null : after == null || !before.SequenceEqual(after);
                text.Append($"PlayerPrefs (`{PrefsLocation}`): {(changed ? "changed" : "unchanged")}");
                if (changed && restore)
                {
                    PlayerPrefs.DeleteAll();
                    if (before != null)
                        File.WriteAllBytes(PrefsLocation, before);
                    else if (File.Exists(PrefsLocation))
                        File.Delete(PrefsLocation);
                    text.Append(", restored (file copy)");
                }
                text.Append('\n');
                return;
            }
            var afterPath = Path.Combine(Folder, "prefs-after.reg");
            Reg($"export \"{PrefsLocation}\" \"{afterPath}\" /y");
            var old = ReadReg(PrefsBefore);
            var now = ReadReg(afterPath);
            var lines = new List<string>();
            var engineKeys = 0;
            foreach (var key in old.Keys.Union(now.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                old.TryGetValue(key, out var a);
                now.TryGetValue(key, out var b);
                if (a == b)
                    continue;
                // every play session moves Unity's own session counters; they are restored but not listed
                if (key.StartsWith("unity.", StringComparison.Ordinal) || key.StartsWith("unity_connect.", StringComparison.Ordinal))
                    engineKeys++;
                else
                    lines.Add($"- `{Readable(key)}`: {Show(a)} -> {Show(b)}");
            }
            text.Append($"PlayerPrefs (`{PrefsLocation}`): {(lines.Count == 0 ? "unchanged" : $"{lines.Count} changed")}");
            if (engineKeys > 0)
                text.Append($" (+{engineKeys} Unity session keys)");
            if (lines.Count + engineKeys > 0 && restore)
            {
                Reg($"delete \"{PrefsLocation}\" /f");
                if (File.Exists(PrefsBefore))
                    Reg($"import \"{PrefsBefore}\"");
                text.Append(", restored");
            }
            text.Append('\n');
            foreach (var line in lines.Take(MaxListed))
                text.Append(line).Append('\n');
            if (lines.Count > MaxListed)
                text.Append($"- ... {lines.Count - MaxListed} more\n");
        }

        static void CompareFiles(StringBuilder text, bool restore)
        {
            var before = File.ReadAllLines(FilesBefore).Select(l => l.Split('\t')).Where(p => p.Length == 3)
                .ToDictionary(p => p[0], p => (size: long.Parse(p[1], CultureInfo.InvariantCulture), ticks: long.Parse(p[2], CultureInfo.InvariantCulture)));
            var after = ListFiles();
            var created = after.Keys.Where(k => !before.ContainsKey(k)).OrderBy(k => k).ToList();
            var deleted = before.Keys.Where(k => !after.ContainsKey(k)).OrderBy(k => k).ToList();
            var changed = after.Keys.Where(k => before.TryGetValue(k, out var b) && b != after[k]).OrderBy(k => k).ToList();
            var count = created.Count + deleted.Count + changed.Count;
            text.Append($"Files in `{SaveFolder}`: {(count == 0 ? "unchanged" : $"{created.Count} new, {changed.Count} changed, {deleted.Count} deleted")}");
            var canRestore = Directory.Exists(Backup) || before.Count == 0;
            if (count > 0 && restore)
            {
                if (canRestore)
                {
                    foreach (var file in created)
                        File.Delete(Path.Combine(SaveFolder, file));
                    foreach (var file in changed.Concat(deleted))
                    {
                        var to = Path.Combine(SaveFolder, file);
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.Copy(Path.Combine(Backup, file), to, true);
                    }
                    text.Append(", restored");
                }
                else
                    text.Append($", NOT restored (the folder was over {BackupLimitBytes / (1024 * 1024)} MB, no backup)");
            }
            text.Append('\n');
            var listed = created.Select(f => $"- new `{f}` ({after[f].size} B)")
                .Concat(changed.Select(f => $"- changed `{f}` ({before[f].size} -> {after[f].size} B)"))
                .Concat(deleted.Select(f => $"- deleted `{f}`")).ToList();
            foreach (var line in listed.Take(MaxListed))
                text.Append(line).Append('\n');
            if (listed.Count > MaxListed)
                text.Append($"- ... {listed.Count - MaxListed} more\n");
        }

        static Dictionary<string, (long size, long ticks)> ListFiles()
        {
            var result = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
            if (!Directory.Exists(SaveFolder))
                return result;
            foreach (var path in Directory.GetFiles(SaveFolder, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                result[path.Substring(SaveFolder.Length).TrimStart('/', '\\').Replace('\\', '/')] = (info.Length, info.LastWriteTimeUtc.Ticks);
            }
            return result;
        }

        static void Reg(string arguments)
        {
            var start = new ProcessStartInfo("reg.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var process = Process.Start(start);
            process?.WaitForExit(15000);
        }

        // "name"=value pairs of a reg export; long hex values continue on lines ending with '\'
        static Dictionary<string, string> ReadReg(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path))
                return result;
            var joined = Regex.Replace(File.ReadAllText(path, Encoding.Unicode), @"\\\r?\n\s*", string.Empty);
            foreach (Match match in Regex.Matches(joined, "^\"((?:[^\"\\\\]|\\\\.)*)\"=(.*)$", RegexOptions.Multiline))
                result[match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"")] = match.Groups[2].Value.TrimEnd('\r');
            return result;
        }

        // Unity appends a hash of the key name: score_h12345
        static string Readable(string key) => Regex.Replace(key, @"_h\d+$", string.Empty);

        static string Show(string? value)
        {
            if (value == null)
                return "(none)";
            if (value.StartsWith("dword:", StringComparison.Ordinal))
                return int.Parse(value.Substring(6), NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            var colon = value.IndexOf(':');
            if (colon < 0 || !value.StartsWith("hex", StringComparison.Ordinal))
                return value;
            var bytes = value.Substring(colon + 1).Split(',').Where(b => b.Length > 0)
                .Select(b => byte.Parse(b, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            if (value.StartsWith("hex(b)", StringComparison.Ordinal) && bytes.Length == 8)
                return BitConverter.ToDouble(bytes, 0).ToString("0.###", CultureInfo.InvariantCulture);
            var textBytes = bytes.Length > 0 && bytes[bytes.Length - 1] == 0 ? bytes.Take(bytes.Length - 1).ToArray() : bytes;
            var s = Encoding.UTF8.GetString(textBytes);
            return "\"" + (s.Length > 60 ? s.Substring(0, 60) + "..." : s) + "\"";
        }
    }
}
