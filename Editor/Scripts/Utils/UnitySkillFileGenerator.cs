#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Feeder.ToolHost;
using Feeder.ToolHost.Skills;
using Feeder.MCP.Editor.API;
using Feeder.Logging;

namespace Feeder.MCP.Editor.Utils
{
    /// <summary>
    /// Skill file generator tuned for the embedded Feeder MCP package.
    /// </summary>
    public class UnitySkillFileGenerator : SkillFileGenerator
    {
        public UnitySkillFileGenerator() : base()
        {
        }
        public UnitySkillFileGenerator(ILogger? logger = null) : base(logger)
        {
        }

        /// <summary>
        /// Authorization is handled automatically by the CLI from the project config file.
        /// No need to show a separate authorization example in SKILL.md.
        /// </summary>
        public override bool IncludeAuthorizationExample => false;

        /// <summary>
        /// Description is already in the YAML front-matter — skip the duplicate paragraph
        /// after the title to save tokens.
        /// </summary>
        public override bool IncludeDescriptionBody => false;

        /// <summary>
        /// Descriptions are already shown in the parameter table — strip them from the
        /// Input JSON Schema to save tokens.
        /// </summary>
        public override bool IncludeInputSchemaPropertyDescriptions => false;

        bool vfxToolsEnabled;

        /// <inheritdoc/>
        public override bool Generate(IEnumerable<IRunTool> tools, string skillsPath, string host)
        {
            vfxToolsEnabled = tools?.Any(t => t.Name.StartsWith("vfx-")) == true;
            return base.Generate(tools!, skillsPath, host);
        }

        /// <summary>
        /// The experimental VFX skill is written only while a vfx-* tool is enabled, so agents of
        /// projects that never use VFX Studio do not carry it.
        /// </summary>
        public override bool Generate(IEnumerable<ISkillContent> skills, string skillsPath)
        {
            RemoveRetiredGuides(skillsPath);
            if (vfxToolsEnabled || skills == null)
                return base.Generate(skills!, skillsPath);
            var vfx = skills.Where(s => s.Name == Skill_Vfx.SkillId).ToList();
            if (vfx.Count > 0)
                base.Delete(vfx, skillsPath);
            return base.Generate(skills.Except(vfx), skillsPath);
        }

        // guide skills carry no own-skill mark, so nothing else removes one that was renamed. Only our old text is
        // removed: the playtest tool's own skill now lives in matrix-playtest and carries the mark.
        static readonly (string Folder, string DescriptionStart)[] RetiredGuides =
        {
            ("matrix-playtest", "Play-test the game with the 'playtest' tool"),
            ("matrix-playtesting", "Play-test the game with the 'playtest' tool"),
        };

        internal static void RemoveRetiredGuides(string skillsPath)
        {
            foreach (var (folder, start) in RetiredGuides)
            {
                var dir = Path.Combine(skillsPath, folder);
                var file = Path.Combine(dir, "SKILL.md");
                try
                {
                    if (!File.Exists(file) || IsOwnSkillFile(file) || !File.ReadAllText(file).Contains($"description: \"{start}"))
                        continue;
                    File.Delete(file);
                    if (Directory.GetFileSystemEntries(dir).Length == 0)
                        Directory.Delete(dir);
                }
                catch (System.Exception e) when (e is IOException or System.UnauthorizedAccessException)
                {
                    UnityEngine.Debug.LogWarning($"[Matrix] Could not remove the retired skill {dir}: {e.Message}");
                }
            }
        }

        /// <inheritdoc/>
        protected override void BuildHowToCallHeading(StringBuilder sb)
        {
            // sb.AppendLine("## Use command line");
            // sb.AppendLine();
        }

        /// <inheritdoc/>
        protected override void BuildToolCommand(StringBuilder sb, IRunTool tool, string host, string inputExample)
        {
            sb.AppendLine("Call this tool through the MCP client connected to the local Matrix Bridge server.");
            sb.AppendLine();
            sb.AppendLine("Example input:");
            sb.AppendLine("```json");
            sb.AppendLine(inputExample);
            sb.AppendLine("```");
            sb.AppendLine();
            AppendInputFileHint(sb, tool, host, inputExample);
            sb.AppendLine($"Read the /{Skill_InitialSetup.SkillId} skill for local connection setup.");
            sb.AppendLine();
        }

        /// <inheritdoc/>
        protected override void AppendInputFileHint(StringBuilder sb, IRunTool tool, string host, string inputExample)
        {
            if (inputExample == "{}")
                return;
            sb.AppendLine("> For complex input, keep the payload as valid JSON and send it through your connected MCP client.");
            sb.AppendLine();
        }
    }
}
