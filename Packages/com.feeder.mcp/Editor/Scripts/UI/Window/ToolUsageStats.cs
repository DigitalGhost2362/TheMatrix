#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Feeder.MCP.Editor.UI
{
    public sealed class ToolUsage
    {
        public int Calls7 { get; set; }
        public int Calls30 { get; set; }
        public int Errors30 { get; set; }
        public int ViaToolCall30 { get; set; }
        public DateTimeOffset? LastUsed { get; set; }

        public float ErrorRate => Calls30 == 0 ? 0f : (float)Errors30 / Calls30;
    }

    // the server writes Library/MatrixUsage/usage-YYYY-MM.jsonl; the window reads it on open and on Refresh only
    public static class ToolUsageStats
    {
        public const string Folder = "Library/MatrixUsage";

        static readonly Regex Field = new Regex("\"(at|tool|via|status)\":(?:\"((?:[^\"\\\\]|\\\\.)*)\"|null)", RegexOptions.Compiled);

        public static IReadOnlyDictionary<string, ToolUsage> Load(string projectRoot, DateTimeOffset now)
        {
            var folder = Path.Combine(projectRoot, Folder);
            if (!Directory.Exists(folder))
                return new Dictionary<string, ToolUsage>();
            var months = new[] { now, now.AddMonths(-1) }.Select(at => Path.Combine(folder, $"usage-{at.UtcDateTime:yyyy-MM}.jsonl")).Distinct();
            var lines = new List<string>();
            foreach (var file in months.Where(File.Exists))
            {
                try
                {
                    // the server appends while the window reads
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                        lines.Add(line);
                }
                catch (IOException)
                {
                }
            }
            return Aggregate(lines, now);
        }

        public static IReadOnlyDictionary<string, ToolUsage> Aggregate(IEnumerable<string> lines, DateTimeOffset now)
        {
            var result = new Dictionary<string, ToolUsage>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                string? tool = null, status = null, via = null;
                DateTimeOffset? at = null;
                foreach (Match m in Field.Matches(line))
                {
                    var value = m.Groups[2].Success ? Regex.Unescape(m.Groups[2].Value) : null;
                    switch (m.Groups[1].Value)
                    {
                        case "tool": tool = value; break;
                        case "status": status = value; break;
                        case "via": via = value; break;
                        case "at":
                            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                                at = parsed;
                            break;
                    }
                }
                if (string.IsNullOrEmpty(tool) || at == null)
                    continue;
                var age = now - at.Value;
                if (age > TimeSpan.FromDays(30))
                    continue;

                if (!result.TryGetValue(tool!, out var usage))
                    result[tool!] = usage = new ToolUsage();
                usage.Calls30++;
                if (age <= TimeSpan.FromDays(7))
                    usage.Calls7++;
                if (status == "error" || status == "timeout")
                    usage.Errors30++;
                if (!string.IsNullOrEmpty(via))
                    usage.ViaToolCall30++;
                if (usage.LastUsed == null || at > usage.LastUsed)
                    usage.LastUsed = at;
            }
            return result;
        }

        public static string Describe(ToolUsage? usage, DateTimeOffset now)
        {
            if (usage == null || usage.Calls30 == 0)
                return "not used in 30 days";
            var text = $"{usage.Calls7} calls / 7d - {usage.Calls30} / 30d - last {Ago(now - usage.LastUsed!.Value)}";
            if (usage.Errors30 > 0)
                text += $" - {usage.ErrorRate:P0} errors";
            return text;
        }

        static string Ago(TimeSpan span)
            => span.TotalMinutes < 1 ? "just now"
                : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m ago"
                : span.TotalDays < 1 ? $"{(int)span.TotalHours}h ago"
                : $"{(int)span.TotalDays}d ago";
    }
}
