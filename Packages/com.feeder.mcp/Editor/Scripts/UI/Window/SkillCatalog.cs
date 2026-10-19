#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Feeder.MCP.Editor.UI
{
    internal enum SkillRisk
    {
        ReadOnly,
        Writes,
        Destructive,
    }

    internal enum SkillCost
    {
        Lean,
        Moderate,
        Heavy,
    }

    internal readonly struct ToolFacts
    {
        public ToolFacts(string name, string? title, string? text, bool? readOnlyHint = null, bool? destructiveHint = null, int tokens = 0)
        {
            Name = name;
            Title = title;
            Text = text;
            ReadOnlyHint = readOnlyHint;
            DestructiveHint = destructiveHint;
            Tokens = tokens;
        }

        public string Name { get; }
        public string? Title { get; }
        public string? Text { get; }
        public bool? ReadOnlyHint { get; }
        public bool? DestructiveHint { get; }
        public int Tokens { get; }
    }

    internal sealed class SkillGroup
    {
        public SkillGroup(string name, string mono, string blurb)
        {
            Name = name;
            Mono = mono;
            Blurb = blurb;
        }

        public string Name { get; }
        public string Mono { get; }
        public string Blurb { get; }
    }

    internal sealed class SkillInfo
    {
        public SkillInfo(string name, string title, SkillGroup group, string label, string summary, string description, SkillRisk risk, int tokens, bool isCore, IReadOnlyList<string> companions)
        {
            Name = name;
            Title = title;
            Group = group;
            Label = label;
            Summary = summary;
            Description = description;
            Risk = risk;
            Tokens = tokens;
            IsCore = isCore;
            Companions = companions;
            SearchText = $"{label} {name} {summary} {group.Name}".ToLowerInvariant();
        }

        public string Name { get; }
        public string Title { get; }
        public SkillGroup Group { get; }
        public string Label { get; }
        public string Summary { get; }
        public string Description { get; }
        public SkillRisk Risk { get; }
        public int Tokens { get; }
        public bool IsCore { get; }
        public IReadOnlyList<string> Companions { get; }
        public string SearchText { get; }
    }

    internal static class SkillCatalog
    {
        public const int LeanBelow = 12000;
        public const int HeavyFrom = 30000;

        const int MaxCompanions = 4;
        const int SummaryLimit = 150;
        const int MinGist = 20;

        static readonly SkillGroup[] _known =
        {
            new SkillGroup("Query", "RQ", "Read and change many objects with one query."),
            new SkillGroup("Scenes", "SC", "Open, save and switch scenes."),
            new SkillGroup("GameObjects", "GO", "Find, create and edit objects in the open scene."),
            new SkillGroup("Assets", "AS", "Search, read and edit files in the project."),
            new SkillGroup("Prefabs", "PF", "Open, edit and place prefabs."),
            new SkillGroup("Code & Tests", "CS", "Read and write scripts, run C#, run tests."),
            new SkillGroup("Vision", "VW", "Screenshots and UI Toolkit inspection."),
            new SkillGroup("Editor", "ED", "Play mode, selection and the console."),
            new SkillGroup("Packages", "PK", "Search, install and remove packages."),
            new SkillGroup("Profiler", "PR", "Capture frames and read performance data."),
            new SkillGroup("Matrix", "MX", "Let agents manage their own skills."),
        };

        // first title segment -> known group, and how many leading segments the row label drops
        static readonly Dictionary<string, (string Group, int Drop)> _byFirstSegment = new Dictionary<string, (string Group, int Drop)>
        {
            ["Query"] = ("Query", 1),
            ["Scene"] = ("Scenes", 1),
            ["GameObject"] = ("GameObjects", 1),
            ["Object"] = ("GameObjects", 0),
            ["Assets"] = ("Assets", 1),
            ["Script"] = ("Code & Tests", 0),
            ["Method C#"] = ("Code & Tests", 0),
            ["Type"] = ("Code & Tests", 0),
            ["Tests"] = ("Code & Tests", 0),
            ["Screenshot"] = ("Vision", 0),
            ["UI"] = ("Vision", 0),
            ["Editor"] = ("Editor", 1),
            ["Console"] = ("Editor", 0),
            ["Package Manager"] = ("Packages", 0),
            ["Profiler"] = ("Profiler", 1),
            ["Tool"] = ("Matrix", 0),
            ["Skill (Tool)"] = ("Matrix", 0),
            ["Ping"] = ("Matrix", 0),
        };

        static readonly Regex _sentenceEnd = new Regex(@"\.(\s|$)", RegexOptions.Compiled);
        static readonly Regex _parenthetical = new Regex(@"\s*\([^()]*\)", RegexOptions.Compiled);
        static readonly Regex _toolName = new Regex(@"'([a-z0-9]+(?:-[a-z0-9]+)+)'", RegexOptions.Compiled);

        public static IReadOnlyList<SkillInfo> Build(IEnumerable<ToolFacts> tools, IReadOnlyCollection<string> core)
        {
            var list = tools.Where(tool => !string.IsNullOrEmpty(tool.Name)).ToList();
            var names = new HashSet<string>(list.Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);
            var custom = new Dictionary<string, SkillGroup>();
            var infos = new List<SkillInfo>(list.Count);
            foreach (var tool in list)
            {
                var segments = Segments(tool.Title, tool.Name);
                var group = GroupOf(segments, custom, out var drop);
                var text = tool.Text ?? string.Empty;
                infos.Add(new SkillInfo(
                    tool.Name,
                    string.Join(" / ", segments),
                    group,
                    LabelOf(segments, drop),
                    Summary(text),
                    text,
                    RiskOf(tool.ReadOnlyHint, tool.DestructiveHint),
                    tool.Tokens,
                    core.Contains(tool.Name, StringComparer.OrdinalIgnoreCase),
                    Companions(text, names, tool.Name)));
            }

            return infos
                .OrderBy(info => GroupOrder(info.Group))
                .ThenBy(info => info.Group.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(info => info.Risk)
                .ThenBy(info => info.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<SkillGroup> GroupsOf(IEnumerable<SkillInfo> infos) => infos
            .Select(info => info.Group)
            .Distinct()
            .OrderBy(GroupOrder)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        public static string[] Segments(string? title, string name) => string.IsNullOrWhiteSpace(title)
            ? new[] { name }
            : title!.Split('/').Select(part => part.Trim()).Where(part => part.Length > 0).DefaultIfEmpty(name).ToArray();

        public static SkillGroup GroupOf(string[] segments, IDictionary<string, SkillGroup> custom, out int drop)
        {
            var first = segments[0];
            if (_byFirstSegment.TryGetValue(first, out var entry))
            {
                var prefab = entry.Group == "Assets" && segments.Length > 2 && segments[1] == "Prefab";
                drop = prefab ? 2 : entry.Drop;
                var name = prefab ? "Prefabs" : entry.Group;
                return _known.First(known => known.Name == name);
            }

            drop = segments.Length > 1 ? 1 : 0;
            var customName = segments.Length > 1 ? first : "Other";
            if (!custom.TryGetValue(customName, out var group))
            {
                var mono = customName.Length >= 2 ? customName.Substring(0, 2).ToUpperInvariant() : customName.ToUpperInvariant();
                group = new SkillGroup(customName, mono, string.Empty);
                custom[customName] = group;
            }
            return group;
        }

        public static string LabelOf(string[] segments, int drop)
        {
            var kept = segments.Skip(drop).ToArray();
            return string.Join(" / ", kept.Length > 0 ? kept : segments);
        }

        public static SkillRisk RiskOf(bool? readOnly, bool? destructive)
        {
            if (destructive == true)
                return SkillRisk.Destructive;
            return readOnly == true ? SkillRisk.ReadOnly : SkillRisk.Writes;
        }

        public static string Summary(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var flat = _parenthetical.Replace(text!.Replace("`", string.Empty).Replace('\r', ' ').Replace('\n', ' '), string.Empty).Trim();
            var match = _sentenceEnd.Match(flat);
            var sentence = match.Success ? flat.Substring(0, match.Index + 1) : flat;
            var dash = sentence.IndexOf(" \u2014 ", StringComparison.Ordinal);
            if (dash >= MinGist)
                sentence = sentence.Substring(0, dash) + ".";
            return sentence.Length > SummaryLimit ? sentence.Substring(0, SummaryLimit - 3).TrimEnd() + "..." : sentence;
        }

        public static IReadOnlyList<string> Companions(string? text, ISet<string> toolNames, string self)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            return _toolName.Matches(text!)
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .Where(name => toolNames.Contains(name) && !string.Equals(name, self, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .Take(MaxCompanions)
                .ToList();
        }

        public static SkillCost CostOf(int tokens) => tokens < LeanBelow ? SkillCost.Lean : tokens < HeavyFrom ? SkillCost.Moderate : SkillCost.Heavy;

        public static string FormatTokens(int tokens)
        {
            if (tokens < 1000)
                return tokens.ToString(CultureInfo.InvariantCulture);

            var thousands = tokens / 1000.0;
            var text = tokens < 10000
                ? thousands.ToString("0.#", CultureInfo.InvariantCulture)
                : Math.Round(thousands, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
            return text + "k";
        }

        static int GroupOrder(SkillGroup group)
        {
            var index = Array.IndexOf(_known, group);
            return index >= 0 ? index : _known.Length;
        }
    }
}
