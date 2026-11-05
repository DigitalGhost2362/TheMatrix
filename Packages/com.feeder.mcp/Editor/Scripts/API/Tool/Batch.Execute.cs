#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Batch
    {
        public const string BatchExecuteToolId = "batch-execute";

        [AiTool
        (
            BatchExecuteToolId,
            Title = "Batch / Execute",
            Enabled = false
        )]
        [Description("Runs up to 100 different Unity tools in order in one call, for example create an object, add a component, assign a material. " +
            "All commands are validated first (unknown, disabled or forbidden tools run nothing). " +
            "stopOnError (default true) stops at the first failing command. " +
            "atomic=true makes the batch one undo step ('Matrix batch') and, if any command fails, reverts it with the undo stack (implies stopOnError); " +
            "only undo-recorded changes revert, not disk writes or saved assets. " +
            "Not allowed inside a batch: batch-execute, tool-call, editor-health and the editor-lock tools. " +
            "Tools that finish later (compile, assets-refresh, test runs) count as an error and stop the batch: run them alone. " +
            "Limits: the batch counts as an editor-changing call for the editor lock even when every command only reads, " +
            "and the commands skip your client's per-tool permission rules, only batch-execute itself is checked.")]
        public async Task<BatchResult> Execute
        (
            [Description("Commands run in order. Each is {\"tool\": name, \"arguments\": {...}}. 1 to 100.")]
            BatchCommand[] commands,
            [Description("Stop at the first failing command. Default: true")]
            bool? stopOnError = true,
            [Description("One undo step, reverted when a command fails. Default: false")]
            bool? atomic = false,
            [RequestID]
            string? requestId = null
        )
        {
            var manager = MatrixPluginEditor.Instance.Tools
                ?? throw new InvalidOperationException("Tool manager is not available. MatrixPluginEditor may not be initialized.");
            var all = manager.GetAllTools().ToList();

            string? Resolve(string name)
            {
                var tool = all.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
                if (tool == null)
                {
                    var matches = all.Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                    tool = matches.Count == 1 ? matches[0] : null;
                }
                return tool != null && manager.IsToolEnabled(tool.Name) ? tool.Name : null;
            }

            return await ToolBatch.RunAsync(
                commands,
                stopOnError ?? true,
                atomic ?? false,
                Resolve,
                (name, args, index) =>
                {
                    var tool = all.First(t => t.Name == name);
                    return manager.RunTool(tool, $"{requestId ?? "batch"}#{index}", args, CancellationToken.None);
                },
                beginGroup: () => MainThread.Instance.Run(() =>
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(ToolBatch.UndoName);
                    return Undo.GetCurrentGroup();
                }),
                commitGroup: group => MainThread.Instance.Run(() => Undo.CollapseUndoOperations(group)),
                revertGroup: group => MainThread.Instance.Run(() => Undo.RevertAllDownToGroup(group)));
        }
    }
}
