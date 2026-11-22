#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Feeder.MCP.Editor.HyperTesting
{
    // copies the journal templates, the orchestrator skill and one subagent file per clone into the project
    public static class HyperTestingWorkspace
    {
        const string IgnoreHeader = "# Matrix Hyper Testing (experimental): machine-specific files";

        static readonly string[] IgnoredPaths =
        {
            "/HyperTesting/Runs/",
            "/HyperTesting/Journal/ram-samples*.csv",
            "/.claude/agents/hyper-playtester-*.md",
        };

        // the guide skill sits in the skills folder Matrix uses, plus that of every agent that gets skill files
        static IEnumerable<string> IgnoredSkillPaths()
        {
            var roots = new List<string> { MatrixPluginEditor.SkillsPath };
            foreach (var agentId in MatrixPluginEditor.AutoConfigureAgentIds)
            {
                var agent = Feeder.MCP.Editor.UI.AiAgentCatalog.GetByAgentId(agentId);
                if (agent?.SupportsSkills == true && MatrixPluginEditor.IsAutoGenerateSkills(agent.AgentId) && !string.IsNullOrEmpty(agent.SkillsPath))
                    roots.Add(agent.SkillsPath!);
            }
            foreach (var root in roots)
            {
                var relative = ProjectRelative(root);
                if (relative != null)
                    yield return "/" + relative + "/" + HyperTestingPaths.SkillId + "/";
            }
        }

        // null for a folder outside the project, which no .gitignore line can cover
        static string? ProjectRelative(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return null;
            var project = Path.GetFullPath(HyperTestingPaths.ProjectRoot).TrimEnd('\\', '/');
            var full = Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(project, folder)).TrimEnd('\\', '/');
            if (!full.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;
            return full.Substring(project.Length + 1).Replace('\\', '/');
        }

        static string TemplatesRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HyperTestingWorkspace).Assembly);
                var root = info?.resolvedPath ?? Path.GetFullPath("Packages/com.feeder.mcp");
                return Path.Combine(root, "HyperTesting~");
            }
        }

        public static void Scaffold()
        {
            var source = Path.Combine(TemplatesRoot, "workspace");
            if (!Directory.Exists(source))
            {
                Debug.LogError($"{HyperTestingActivation.LogPrefix} template folder missing: {source}");
                return;
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart('\\', '/');
                var destination = Path.Combine(HyperTestingPaths.Workspace, relative);
                if (File.Exists(destination))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            Directory.CreateDirectory(HyperTestingPaths.Runs);
            new HyperTestingConfig().SaveIfMissing();
            Directory.CreateDirectory(HyperTestingPaths.AgentsFolder);
            WriteSkill();
            WriteTools();
            AppendGitIgnore();
        }

        public static void WriteSkill()
        {
            var template = Path.Combine(TemplatesRoot, "skill", "SKILL.md");
            if (!File.Exists(template))
            {
                Debug.LogError($"{HyperTestingActivation.LogPrefix} skill template missing: {template}");
                return;
            }
            Directory.CreateDirectory(HyperTestingPaths.SkillFolder);
            File.WriteAllText(Path.Combine(HyperTestingPaths.SkillFolder, "SKILL.md"), File.ReadAllText(template));
            RemoveOldGuide();
        }

        // Older versions wrote this guide into "matrix-hyper-testing", the folder of the hyper-testing tool's own skill.
        // Remove it only when it is that old guide: never a file the tool skill generator wrote.
        static void RemoveOldGuide()
        {
            try
            {
                var folder = Path.Combine(MatrixPluginEditor.SkillsRootFolderAbsolutePath, HyperTestingPaths.OldSkillId);
                var file = Path.Combine(folder, "SKILL.md");
                if (!File.Exists(file) || Feeder.ToolHost.Skills.SkillFileGenerator.IsOwnSkillFile(file))
                    return;
                var text = File.ReadAllText(file);
                if (!text.Contains("name: " + HyperTestingPaths.OldSkillId + "\n") && !text.Contains("name: " + HyperTestingPaths.OldSkillId + "\r\n"))
                    return;
                if (!text.Contains("EXPERIMENTAL Hyper Testing Mode"))
                    return;
                File.Delete(file);
                if (Directory.GetFileSystemEntries(folder).Length == 0)
                    Directory.Delete(folder);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{HyperTestingActivation.LogPrefix} could not remove the old guide skill: {ex.Message}");
            }
        }

        // the scripts that drive a clone over HTTP when a subagent has no MCP tools for it; rewritten on every up so fixes arrive with the package
        static void WriteTools()
        {
            var source = Path.Combine(TemplatesRoot, "tools");
            if (!Directory.Exists(source))
            {
                Debug.LogError($"{HyperTestingActivation.LogPrefix} tools template folder missing: {source}");
                return;
            }
            var destination = Path.Combine(HyperTestingPaths.Workspace, "tools");
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                var text = File.ReadAllText(file);
                // bash refuses a script with CRLF line ends
                WriteIfChanged(Path.Combine(destination, Path.GetFileName(file)), file.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) ? text.Replace("\r\n", "\n") : text);
            }
        }

        public static void WriteAgentFiles(HyperPoolState state)
        {
            if (!HyperTestingActivation.IsEnabled)
            {
                RemoveAgentFiles();
                return;
            }
            WriteSkill();
            WriteTools();
            AppendGitIgnore();
            var template = Path.Combine(TemplatesRoot, "agent", "hyper-playtester.md");
            if (!File.Exists(template))
            {
                Debug.LogError($"{HyperTestingActivation.LogPrefix} agent template missing: {template}");
                return;
            }

            Directory.CreateDirectory(HyperTestingPaths.AgentsFolder);
            var text = File.ReadAllText(template);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in state.clones)
            {
                var content = text
                    .Replace("{{INDEX}}", entry.index.ToString())
                    .Replace("{{PORT}}", entry.port.ToString())
                    .Replace("{{CLONE_ROOT}}", entry.root.Replace('\\', '/'))
                    .Replace("{{ORIGIN_ROOT}}", HyperTestingPaths.ProjectRoot.Replace('\\', '/'));
                var path = Path.Combine(HyperTestingPaths.AgentsFolder, $"{HyperTestingPaths.AgentPrefix}{entry.index}.md");
                wanted.Add(path);
                // an unchanged file keeps its timestamps, which is how AgentFileWarning tells new files from old ones
                WriteIfChanged(path, content);
            }
            foreach (var stale in Directory.GetFiles(HyperTestingPaths.AgentsFolder, HyperTestingPaths.AgentPrefix + "*.md").Where(f => !wanted.Contains(f)))
                File.Delete(stale);
        }

        static void WriteIfChanged(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path) == text)
                return;
            File.WriteAllText(path, text);
        }

        // Claude Code reads .claude/agents once, at start: a file created or changed after that cannot be spawned with its MCP tools
        public static string AgentFileWarning()
        {
            var folder = HyperTestingPaths.AgentsFolder;
            var started = SessionStartUtc(HyperTestingPaths.ProjectRoot);
            if (started == null || !Directory.Exists(folder))
                return string.Empty;
            var late = Directory.GetFiles(folder, HyperTestingPaths.AgentPrefix + "*.md")
                .Where(f => File.GetCreationTimeUtc(f) > started || File.GetLastWriteTimeUtc(f) > started)
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();
            return late.Count == 0
                ? string.Empty
                : $"{string.Join(", ", late)} look newer than the Claude Code session that began at {started.Value.ToLocalTime():HH:mm} (the newest transcript of this project; a resumed session starts later). " +
                  "A session cannot spawn agents that appeared after it started: if the MCP tools of a clone are missing, restart Claude Code or drive the clone over HTTP with HyperTesting/tools/hc.py.";
        }

        // the newest transcript of this project is the live session, and its creation time is when the session began
        static DateTime? SessionStartUtc(string projectRoot)
        {
            try
            {
                var key = Regex.Replace(Path.GetFullPath(projectRoot), "[^A-Za-z0-9]", "-");
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects", key);
                if (!Directory.Exists(folder))
                    return null;
                return new DirectoryInfo(folder).GetFiles("*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.CreationTimeUtc;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void RemoveAgentFiles()
        {
            DeleteAgentFiles();
            if (Directory.Exists(HyperTestingPaths.SkillFolder))
                Directory.Delete(HyperTestingPaths.SkillFolder, true);
            RemoveOldGuide();
        }

        static void DeleteAgentFiles()
        {
            if (!Directory.Exists(HyperTestingPaths.AgentsFolder))
                return;
            foreach (var file in Directory.GetFiles(HyperTestingPaths.AgentsFolder, HyperTestingPaths.AgentPrefix + "*.md"))
                File.Delete(file);
        }

        static void AppendGitIgnore()
        {
            try
            {
                var path = Path.Combine(HyperTestingPaths.ProjectRoot, ".gitignore");
                var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                var present = existing.Split('\n').Select(line => line.Trim()).ToHashSet();
                var missing = IgnoredPaths.Concat(IgnoredSkillPaths()).Distinct().Where(p => !present.Contains(p)).ToList();
                if (missing.Count == 0)
                    return;
                var eol = existing.Contains("\r\n") ? "\r\n" : "\n";
                var text = new StringBuilder();
                if (existing.Length > 0 && !existing.EndsWith("\n"))
                    text.Append(eol);
                text.Append(eol).Append(IgnoreHeader).Append(eol);
                foreach (var line in missing)
                    text.Append(line).Append(eol);
                File.AppendAllText(path, text.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{HyperTestingActivation.LogPrefix} could not update .gitignore: {ex.Message}");
            }
        }
    }
}
