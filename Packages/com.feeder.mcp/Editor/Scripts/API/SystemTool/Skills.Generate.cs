#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using Feeder.ToolHost;
using Feeder.MCP.Utils;
using Feeder.Logging;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Skills
    {
        public const string SkillsGenerateToolId = "unity-skill-generate";
        [AiTool
        (
            SkillsGenerateToolId,
            Title = "Skill (Tool) / Generate All",
            DestructiveHint = false,
            Enabled = false,
            ToolType = McpToolType.System
        )]
        [AiSkillDescription("Regenerate every `SKILL.md` from the project's currently-enabled MCP tools into " +
            "the configured skills folder (or a project-relative override path). " +
            "Writes the YAML `description:` from `[AiSkillDescription]` and the body from `[AiSkillBody]`.")]
        [AiSkillBody("Generate all skills from the existed Tools in the Unity Project.\n\n" +
            "## Inputs\n\n" +
            "- `path` (optional) — project-relative skills folder (e.g. `.claude/skills`). Absolute paths and `..` " +
            "traversal segments are rejected. When null/empty, the editor's configured `SkillsRootFolderAbsolutePath` " +
            "is used.\n\n" +
            "## Behavior\n\n" +
            "Creates the destination folder if missing, then invokes `ToolHostInstance.GenerateSkillFiles(...)` to " +
            "emit `matrix-<tool>/SKILL.md` per enabled MCP tool and remove the ones of disabled tools; a skill folder this package did not write is never changed. The plugin's `SkillsPath` is temporarily swapped to the target " +
            "folder and restored in `finally` so the on-disk configuration is unchanged after the call returns.")]
        [Description("Generate all skills from the existed Tools in the Unity Project.")]
        public void GenerateAll
        (
            [Description("Path to the skills folder. If null or empty, the default path will be used.")]
            string? path = null
        )
        {
            var logger = UnityLoggerFactory.LoggerFactory.CreateLogger<Tool_Skills>();

            var userProvidedPath = !string.IsNullOrEmpty(path);
            if (!userProvidedPath)
                path = MatrixPluginEditor.SkillsRootFolderAbsolutePath;

            if (userProvidedPath)
            {
                if (Path.IsPathRooted(path))
                    throw new ArgumentException(
                        "Path must be a relative path inside the Unity project (e.g. '.claude/skills'). Absolute paths are not allowed.",
                        nameof(path));

                var normalizedPath = path!.Replace('\\', '/');
                if (normalizedPath.Contains("../") || normalizedPath.EndsWith(".."))
                    throw new ArgumentException(
                        "Path must not contain '..' traversal segments. Use a path relative to the project root.",
                        nameof(path));

                path = Path.Combine(MatrixPluginEditor.ProjectRootPath, path);
            }

            if (!Directory.Exists(path))
            {
                logger.LogInformation("Directory does not exist at '{Path}'. Creating it.", path);
                Directory.CreateDirectory(path);
            }

            logger.LogInformation("Generating all skills in folder: {Path}", path);

            var mcpPlugin = MatrixPluginEditor.Instance.ToolHostInstance
                ?? throw new InvalidOperationException(
                    "ToolHostInstance is null. The Feeder MCP plugin may not be fully initialized. " +
                    "Ensure the plugin has completed startup before calling unity-skill-generate.");

            var originalSkillsPath = MatrixPluginEditor.SkillsPath;
            try
            {
                MatrixPluginEditor.SkillsPath = path!;
                mcpPlugin.GenerateSkillFiles(MatrixPluginEditor.ProjectRootPath);
            }
            finally
            {
                MatrixPluginEditor.SkillsPath = originalSkillsPath;
            }
        }
    }
}