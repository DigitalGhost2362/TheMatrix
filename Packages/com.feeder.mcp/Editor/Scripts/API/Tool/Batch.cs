#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using Feeder.Json;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Batch
    {
    }

    public sealed class BatchCommand
    {
        [Description("Name of the Unity tool to run, for example 'rql-mutate'.")]
        public string Tool { get; set; } = string.Empty;

        [Description("Arguments of that tool as a JSON object. Omit for none.")]
        public JsonElement? Arguments { get; set; }
    }

    public sealed class BatchCommandResult
    {
        public int Index { get; set; }
        public string Tool { get; set; } = string.Empty;
        public bool Ok { get; set; }
        public bool RolledBack { get; set; }
        public string? Text { get; set; }
        public string? Structured { get; set; }
        public string? Error { get; set; }
    }

    public sealed class BatchResult
    {
        public int Total { get; set; }
        public int Executed { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int? StoppedAt { get; set; }
        public bool Atomic { get; set; }
        public bool RolledBack { get; set; }
        public string? UndoGroup { get; set; }
        public List<BatchCommandResult> Results { get; set; } = new List<BatchCommandResult>();
    }

    /// <summary>The batch loop, with the tool runner and the undo group injected so it can be tested without Unity tools.</summary>
    public static class ToolBatch
    {
        public const int MaxCommands = 100;
        public const int MaxTextLength = 4000;
        public const string UndoName = "Matrix batch";

        // served by the matrix itself, or a batch inside a batch
        static readonly HashSet<string> Forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Tool_Batch.BatchExecuteToolId,
            "tool-call",
            "editor-health",
            "editor-lock-acquire",
            "editor-lock-release",
            "editor-lock-status",
        };

        /// <param name="resolve">The exact name of an enabled tool, or null when it is unknown or disabled.</param>
        /// <param name="invoke">Runs one tool: name, arguments, command index.</param>
        /// <param name="beginGroup">Opens the undo group and returns its id (atomic only).</param>
        /// <param name="commitGroup">Collapses the group into one undo step.</param>
        /// <param name="revertGroup">Reverts everything down to the group.</param>
        public static async Task<BatchResult> RunAsync
        (
            IReadOnlyList<BatchCommand>? commands,
            bool stopOnError,
            bool atomic,
            Func<string, string?> resolve,
            Func<string, IReadOnlyDictionary<string, JsonElement>?, int, Task<ResponseCallTool>> invoke,
            Func<int>? beginGroup = null,
            Action<int>? commitGroup = null,
            Action<int>? revertGroup = null
        )
        {
            if (commands == null || commands.Count == 0)
                throw new ArgumentException("commands is empty. Give at least one {tool, arguments} command.");
            if (commands.Count > MaxCommands)
                throw new ArgumentException($"A batch holds at most {MaxCommands} commands, got {commands.Count}.");

            var names = new string[commands.Count];
            var arguments = new Dictionary<string, JsonElement>?[commands.Count];
            var problems = new List<string>();
            for (var i = 0; i < commands.Count; i++)
            {
                var requested = commands[i]?.Tool?.Trim();
                if (string.IsNullOrEmpty(requested))
                {
                    problems.Add($"#{i}: tool is missing.");
                    continue;
                }
                if (Forbidden.Contains(requested))
                {
                    problems.Add($"#{i}: '{requested}' cannot run inside a batch (batch inside batch, editor lock and matrix tools are not allowed).");
                    continue;
                }
                var exact = resolve(requested);
                if (exact == null)
                {
                    problems.Add($"#{i}: tool '{requested}' was not found or is disabled.");
                    continue;
                }
                names[i] = exact;
                try
                {
                    arguments[i] = ToArguments(commands[i].Arguments);
                }
                catch (ArgumentException e)
                {
                    problems.Add($"#{i}: {e.Message}");
                }
            }
            if (problems.Count > 0)
                throw new ArgumentException("Nothing was run. " + string.Join(" ", problems));

            var result = new BatchResult { Total = commands.Count, Atomic = atomic };
            var group = 0;
            if (atomic)
            {
                group = beginGroup?.Invoke() ?? 0;
                result.UndoGroup = UndoName;
            }

            var stop = stopOnError || atomic;
            for (var i = 0; i < names.Length; i++)
            {
                var entry = new BatchCommandResult { Index = i, Tool = names[i] };
                try
                {
                    Fill(entry, await invoke(names[i], arguments[i], i));
                }
                catch (Exception e)
                {
                    entry.Ok = false;
                    entry.Error = (e.InnerException ?? e).Message;
                }
                result.Results.Add(entry);
                result.Executed++;
                if (entry.Ok)
                {
                    result.Succeeded++;
                    continue;
                }
                result.Failed++;
                if (stop)
                {
                    result.StoppedAt = i;
                    break;
                }
            }

            if (atomic)
            {
                if (result.Failed > 0)
                {
                    revertGroup?.Invoke(group);
                    result.RolledBack = true;
                    foreach (var entry in result.Results)
                        entry.RolledBack = true;
                }
                else
                {
                    commitGroup?.Invoke(group);
                }
            }
            return result;
        }

        static Dictionary<string, JsonElement>? ToArguments(JsonElement? arguments)
        {
            if (!arguments.HasValue)
                return null;
            var kind = arguments.Value.ValueKind;
            if (kind == JsonValueKind.Null || kind == JsonValueKind.Undefined)
                return null;
            if (kind != JsonValueKind.Object)
                throw new ArgumentException("arguments must be a JSON object.");
            var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in arguments.Value.EnumerateObject())
                map[property.Name] = property.Value.Clone();
            return map;
        }

        static void Fill(BatchCommandResult entry, ResponseCallTool? response)
        {
            if (response == null)
            {
                entry.Error = "The tool returned no result.";
                return;
            }
            var text = new StringBuilder();
            var images = 0;
            foreach (var block in response.Content ?? new List<ContentBlock>())
            {
                var type = (block.Type ?? "text").ToLowerInvariant();
                if (type == "image" || type == "audio")
                    images++;
                else if (!string.IsNullOrEmpty(block.Text))
                    text.Append(text.Length > 0 ? "\n" : string.Empty).Append(block.Text);
            }
            if (images > 0)
                text.Append(text.Length > 0 ? "\n" : string.Empty).Append($"[{images} image/audio block(s) omitted]");
            var message = Truncate(text.ToString());

            if (response.Status == ResponseStatus.Processing)
            {
                entry.Error = "The tool completes asynchronously (compile, domain reload or a long job) and cannot be waited for in a batch: " +
                              "run it alone, outside batch-execute. It may already have started. " + message;
                return;
            }
            if (response.Status == ResponseStatus.Error)
            {
                entry.Error = message.Length > 0 ? message : "The tool failed.";
                return;
            }
            entry.Ok = true;
            entry.Text = message.Length > 0 ? message : null;
            if (response.StructuredContent != null)
                entry.Structured = Truncate(response.StructuredContent.ToJsonString());
        }

        static string Truncate(string text)
            => text.Length <= MaxTextLength ? text : text.Substring(0, MaxTextLength) + "... (truncated)";
    }
}
