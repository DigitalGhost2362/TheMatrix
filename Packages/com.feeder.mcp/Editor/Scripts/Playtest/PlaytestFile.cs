#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Feeder.MCP.Editor.Playtest
{
    internal sealed class PlaytestStep
    {
        public PlaytestStep(string verb, string[] args, int line, string raw)
        {
            Verb = verb;
            Args = args;
            Line = line;
            Raw = raw;
        }

        public string Verb { get; }
        public string[] Args { get; }
        public int Line { get; }
        public string Raw { get; }
    }

    // the project's playtest file: '## Setup' key: value lines, '## Clear screen' and '## Flow: <name>' step lists
    internal sealed class PlaytestFile
    {
        public const string FileName = "Playtest.md";
        static readonly string[] DocsFolders = { "Docs", "docs", "ProjectDocs", "Documentation" };

        // verb -> (min args, max args)
        static readonly Dictionary<string, (int min, int max)> Verbs = new Dictionary<string, (int, int)>
        {
            ["click"] = (1, 2),
            ["expect-active"] = (1, 2),
            ["expect-inactive"] = (1, 2),
            ["expect-visible"] = (1, 2),
            ["expect-hidden"] = (1, 2),
            ["wait"] = (1, 1),
            ["hide"] = (1, 1),
            ["hide-type"] = (1, 1),
            ["shot"] = (1, 2),
            ["call"] = (1, 8),
            ["timescale"] = (1, 1),
            ["run"] = (1, 1),
        };

        public string Path { get; private set; } = string.Empty;
        public Dictionary<string, string> Setup { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<PlaytestStep> ClearScreen { get; } = new List<PlaytestStep>();
        public Dictionary<string, List<PlaytestStep>> Flows { get; } = new Dictionary<string, List<PlaytestStep>>(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors { get; } = new List<string>();

        public string Get(string key, string fallback) => Setup.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

        public float GetFloat(string key, float fallback) =>
            float.TryParse(Get(key, string.Empty), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

        public bool GetBool(string key, bool fallback)
        {
            var value = Get(key, string.Empty).ToLowerInvariant();
            return value is "true" or "on" or "yes" ? true : value is "false" or "off" or "no" ? false : fallback;
        }

        public static string? Find(string projectRoot)
        {
            foreach (var folder in DocsFolders)
            {
                var path = System.IO.Path.Combine(projectRoot, folder, FileName);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        public static PlaytestFile Load(string path)
        {
            var file = new PlaytestFile { Path = path };
            var lines = File.ReadAllLines(path);
            List<PlaytestStep>? steps = null;
            var inSetup = false;
            var inFence = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }
                if (inFence)
                    continue;
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    var title = line.Substring(3).Trim();
                    inSetup = title.Equals("Setup", StringComparison.OrdinalIgnoreCase);
                    steps = null;
                    if (title.Equals("Clear screen", StringComparison.OrdinalIgnoreCase))
                        steps = file.ClearScreen;
                    else if (title.StartsWith("Flow:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = title.Substring(5).Trim();
                        if (file.Flows.ContainsKey(name))
                            file.Errors.Add($"line {i + 1}: flow '{name}' is defined twice");
                        steps = file.Flows[name] = new List<PlaytestStep>();
                    }
                    continue;
                }
                var content = StripComment(line);
                if (inSetup)
                {
                    var colon = content.IndexOf(':');
                    if (colon > 0)
                        file.Setup[content.Substring(0, colon).Trim()] = content.Substring(colon + 1).Trim();
                    continue;
                }
                if (steps == null || !content.StartsWith("- ", StringComparison.Ordinal))
                    continue;
                var tokens = Tokenize(content.Substring(2));
                if (tokens.Count == 0)
                    continue;
                var verb = tokens[0].ToLowerInvariant();
                var args = tokens.Skip(1).ToArray();
                if (!Verbs.TryGetValue(verb, out var range))
                    file.Errors.Add($"line {i + 1}: unknown verb '{verb}' (known: {string.Join(", ", Verbs.Keys)})");
                else if (args.Length < range.min || args.Length > range.max)
                    file.Errors.Add($"line {i + 1}: '{verb}' takes {range.min}-{range.max} arguments, got {args.Length}");
                else
                    steps.Add(new PlaytestStep(verb, args, i + 1, content.Substring(2).Trim()));
            }
            foreach (var flow in file.Flows)
                foreach (var step in flow.Value.Where(s => s.Verb == "run" && !file.Flows.ContainsKey(s.Args[0])))
                    file.Errors.Add($"line {step.Line}: run '{step.Args[0]}' names no flow");
            return file;
        }

        // the flow with every 'run' inlined, after the clear-screen steps
        public List<PlaytestStep> Expand(string flow)
        {
            var result = new List<PlaytestStep>(ClearScreen);
            Inline(flow, result, 0);
            return result;
        }

        void Inline(string flow, List<PlaytestStep> into, int depth)
        {
            if (depth > 8)
                throw new InvalidOperationException($"flow '{flow}': 'run' nests deeper than 8, likely a loop");
            if (!Flows.TryGetValue(flow, out var steps))
                throw new ArgumentException($"no flow '{flow}' in {Path}; flows: {string.Join(", ", Flows.Keys)}");
            foreach (var step in steps)
            {
                if (step.Verb == "run")
                    Inline(step.Args[0], into, depth + 1);
                else
                    into.Add(step);
            }
        }

        public string Describe()
        {
            var text = new StringBuilder();
            text.Append($"{Path}: clear screen {ClearScreen.Count} steps; flows: ");
            text.Append(Flows.Count == 0 ? "none" : string.Join(", ", Flows.Select(f => $"{f.Key} ({f.Value.Count})")));
            if (Errors.Count > 0)
                text.Append("\nerrors:\n").Append(string.Join("\n", Errors));
            return text.ToString();
        }

        static string StripComment(string line)
        {
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                    quoted = !quoted;
                else if (!quoted && line[i] == '#' && i > 0 && line[i - 1] == ' ')
                    return line.Substring(0, i).TrimEnd();
            }
            return line;
        }

        static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            var hadQuote = false;
            foreach (var c in text)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    hadQuote = true;
                    continue;
                }
                if (c == ' ' && !quoted)
                {
                    if (current.Length > 0 || hadQuote)
                        tokens.Add(hadQuote ? "\"" + current + "\"" : current.ToString());
                    current.Clear();
                    hadQuote = false;
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0 || hadQuote)
                tokens.Add(hadQuote ? "\"" + current + "\"" : current.ToString());
            return tokens;
        }
    }
}
