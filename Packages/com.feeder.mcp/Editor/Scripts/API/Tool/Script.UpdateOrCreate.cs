#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public static partial class Tool_Script
    {
        public const string ScriptUpdateOrCreateToolId = "script-update-or-create";
        [AiTool
        (
            ScriptUpdateOrCreateToolId,
            Title = "Script / Update or Create",
            DestructiveHint = true,
            OpenWorldHint = false,
            Enabled = false
        )]
        [AiSkillDescription("Write a `.cs` script file: create it, overwrite it with `content`, or edit it in place with `oldText`/`newText`. " +
            "`expectedSha256` refuses the write when the file changed since you read it. Code is checked before the write — invalid code is rejected " +
            "and the file is left untouched. Refreshes the AssetDatabase and delivers the final result via `requestId` after Unity finishes " +
            "the triggered compilation. Use '" + ScriptReadToolId + "' (`includeSha256`) to inspect existing content first.")]
        [AiSkillBody("Updates or creates script file. Does AssetDatabase.Refresh() at the end. " +
            "Use '" + ScriptReadToolId + "' tool to read existing script files first.\n\n" +
            "## Inputs\n\n" +
            "- `filePath` — required `.cs` path.\n" +
            "- `content` — full C# source (create or overwrite). Never combine with `oldText`.\n" +
            "- `oldText` / `newText` — partial edit: `oldText` must occur exactly once (or pass `replaceAll`); `newText` replaces it. " +
            "Line endings of both are adapted to the file. A call with neither `content` nor `oldText` is an error and writes nothing.\n" +
            "- `expectedSha256` — sha256 of the file you based the change on (from '" + ScriptReadToolId + "' `includeSha256`, or the previous write). " +
            "A different file answers `[stale_file current_sha256=...]` and nothing is written.\n" +
            "- `validate` — `syntax` (default), `semantic` (compiles with all loaded references; blocks the write on errors, " +
            "but a part of a partial class can report false errors) or `none`.\n" +
            "- `requestId` — required for the processing/delivered-later contract.\n\n" +
            "## Behavior\n\n" +
            "Creates missing parent directories and writes atomically (temp file, then replace), keeping the file's BOM and line endings. " +
            "The result carries the new `sha256`. Content identical to the file on disk is not written and answers `Success` at once; " +
            "a refresh that starts no compilation (file outside any assembly) delivers the result right after the refresh.")]
        [Description("Updates or creates script file: full `content`, or a partial edit with `oldText`/`newText` (unique match). " +
            "`expectedSha256` guards against overwriting a newer file. Does AssetDatabase.Refresh() at the end. " +
            "Use '" + ScriptReadToolId + "' tool to read existing script files first.")]
        public static ResponseCallTool UpdateOrCreate
        (
            [Description("The path to the file. Sample: \"Assets/Scripts/MyScript.cs\".")]
            string filePath,
            [Description("C# code - full content of the file. Omit when editing with oldText/newText.")]
            string? content = null,
            [RequestID]
            string? requestId = null,
            [Description("sha256 of the file content you based this change on. Mismatch returns [stale_file current_sha256=...] and writes nothing.")]
            string? expectedSha256 = null,
            [Description("Partial edit: exact text to replace. Must occur once unless replaceAll is true.")]
            string? oldText = null,
            [Description("Partial edit: the replacement for oldText (may be empty to delete it).")]
            string? newText = null,
            [Description("Replace every occurrence of oldText instead of requiring a unique one.")]
            bool replaceAll = false,
            [Description("Check before writing: \"syntax\" (default), \"semantic\" (compile with all references, blocks on errors) or \"none\".")]
            string validate = "syntax"
        )
        {
            if (requestId == null || string.IsNullOrWhiteSpace(requestId))
                return ResponseCallTool.Error("[Error] Original request with valid RequestID must be provided.");

            if (string.IsNullOrEmpty(filePath))
                return ResponseCallTool.Error(Error.ScriptPathIsEmpty()).SetRequestID(requestId);

            if (!filePath.EndsWith(".cs"))
                return ResponseCallTool.Error(Error.FilePathMustEndsWithCs()).SetRequestID(requestId);

            validate = string.IsNullOrEmpty(validate) ? "syntax" : validate.ToLowerInvariant();
            if (validate != "syntax" && validate != "semantic" && validate != "none")
                return ResponseCallTool.Error($"[Error] validate must be \"syntax\", \"semantic\" or \"none\", got \"{validate}\".").SetRequestID(requestId);

            var editing = !string.IsNullOrEmpty(oldText);
            if (editing && content != null)
                return ResponseCallTool.Error("[Error] Pass either 'content' (whole file) or 'oldText'/'newText' (partial edit), not both.").SetRequestID(requestId);
            if (!editing && content == null)
                return ResponseCallTool.Error("[Error] Nothing to write: pass 'content' for the whole file, or 'oldText' and 'newText' for an edit. The file was not touched.").SetRequestID(requestId);
            if (editing && newText == null)
                return ResponseCallTool.Error("[Error] 'newText' is required with 'oldText' (use an empty string to delete the text). The file was not touched.").SetRequestID(requestId);

            var exists = File.Exists(filePath);
            var currentBytes = exists ? File.ReadAllBytes(filePath) : Array.Empty<byte>();
            var currentHash = exists ? ScriptEdit.Sha256(currentBytes) : "none";
            if (!string.IsNullOrEmpty(expectedSha256) && !string.Equals(expectedSha256, currentHash, StringComparison.OrdinalIgnoreCase))
                return ResponseCallTool.Error($"[stale_file current_sha256={currentHash}] {filePath} changed since you read it. " +
                    "Read it again, redo the change on the current text, and pass the new hash.").SetRequestID(requestId);

            var bom = exists && ScriptEdit.HasBom(currentBytes);
            var currentText = exists ? ScriptEdit.ReadText(currentBytes) : string.Empty;
            string finalText;
            if (editing)
            {
                if (!exists)
                    return ResponseCallTool.Error($"[Error] {Error.ScriptFileNotFound(filePath)} A partial edit needs an existing file.").SetRequestID(requestId);
                if (!ScriptEdit.Replace(currentText, oldText!, newText!, replaceAll, out finalText, out var editError))
                    return ResponseCallTool.Error(editError!).SetRequestID(requestId);
            }
            else
            {
                finalText = exists ? ScriptEdit.ToEol(content!, ScriptEdit.UsesCrlf(currentText)) : content!;
            }

            if (exists && finalText == currentText)
                return ResponseCallTool.Success($"[Success] Script unchanged: {filePath}. Nothing to compile. sha256={currentHash}").SetRequestID(requestId);

            if (validate != "none")
            {
                var semantic = validate == "semantic";
                if (!CheckSource(finalText, semantic, out var errors, out var skipped))
                    return ResponseCallTool.Error($"[Error] Invalid C# ({validate}):\n{string.Join("\n", errors.Select(ScriptEdit.FormatDiagnostic))}").SetRequestID(requestId);
                if (skipped != null)
                    UnityEngine.Debug.LogWarning($"C# {validate} pre-check skipped, Unity will report real errors on compile: {skipped}");
            }

            var dirPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            ScriptEdit.WriteAtomic(filePath, finalText, bom);
            var newHash = ScriptEdit.Sha256(File.ReadAllBytes(filePath));

            var scriptWord = (exists ? "Script updated" : "Script created") + $" (sha256={newHash})";

            MainThread.Instance.RunAsync(async () =>
            {
                await Task.Yield();

                // Schedule notification to be sent after compilation completes (survives domain reload)
                ScriptUtils.SchedulePostCompilationNotification(requestId, filePath, $"{scriptWord}");

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ScriptUtils.CompletePostCompilationNotificationIfIdle(requestId);
            });

            return ResponseCallTool.Processing($"{scriptWord}. Refreshing AssetDatabase and waiting for compilation to complete...").SetRequestID(requestId);
        }
    }
}
