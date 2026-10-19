#nullable enable
using Feeder.MCP.Data;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Utils;
using Feeder.Logging;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets
    {
        public const string AssetsDeleteToolId = "assets-delete";
        [AiTool
        (
            AssetsDeleteToolId,
            Title = "Assets / Delete",
            DestructiveHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Delete the assets at the given project paths. " +
            "IRREVERSIBLE — deleted files are permanently removed from disk (no trash/undo), so verify with '" +
            Tool_Rql.QueryToolId + "' first. Refreshes the AssetDatabase at the end.")]
        [AiSkillBody("Delete the assets at paths from the project. " +
            "Does AssetDatabase.Refresh() at the end. " +
            "Use '" + Tool_Rql.QueryToolId + "' tool to find assets before deleting.\n\n" +
            "## Warning\n\n" +
            "This is IRREVERSIBLE: `AssetDatabase.DeleteAssets` removes the files from disk permanently — " +
            "there is no trash and no undo. Double-check each path with '" + Tool_Rql.QueryToolId + "' before calling.\n\n" +
            "## Inputs\n\n" +
            "- `paths` — project-relative asset paths to delete. Must be non-empty.\n\n" +
            "## Behavior\n\n" +
            "Routes through `AssetDatabase.DeleteAssets`, which deletes the batch atomically. " +
            "Paths Unity reports as failed are surfaced in `response.Errors`; successfully deleted paths " +
            "are surfaced in `response.DeletedPaths`. The tool is destructive (removes files from disk).")]
        [Description("Delete the assets at paths from the project. " +
            "Does AssetDatabase.Refresh() at the end. " +
            "Use '" + Tool_Rql.QueryToolId + "' tool to find assets before deleting.")]
        public DeleteAssetsResponse Delete
        (
            [Description("The paths of the assets")]
            string[] paths
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var logger = UnityLoggerFactory.LoggerFactory.CreateLogger<Tool_Assets>();

                if (paths.Length == 0)
                    throw new System.Exception(Error.SourcePathsArrayIsEmpty());

                logger.LogInformation("Deleting {Count} asset(s): {Paths}", paths.Length, string.Join(", ", paths));

                var response = new DeleteAssetsResponse();
                var outFailedPaths = new List<string>();
                var success = AssetDatabase.DeleteAssets(paths, outFailedPaths);

                if (!success)
                {
                    response.Errors ??= new();
                    foreach (var failedPath in outFailedPaths)
                    {
                        logger.LogWarning("Failed to delete asset at '{Path}'", failedPath);
                        response.Errors.Add($"Failed to delete asset at {failedPath}.");
                    }
                }

                // Add successfully deleted paths
                foreach (var path in paths)
                {
                    if (!outFailedPaths.Contains(path))
                    {
                        logger.LogInformation("Successfully deleted asset at '{Path}'", path);
                        response.DeletedPaths ??= new();
                        response.DeletedPaths.Add(path);
                    }
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EditorUtils.RepaintAllEditorWindows();

                return response;
            });
        }

    }
}