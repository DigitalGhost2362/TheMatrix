#nullable enable

using System;
using System.Collections.Generic;
using Feeder.Json;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>
    /// Parses `codex exec --json` JSONL. Unknown events remain non-fatal so a Codex CLI
    /// update cannot take a Matrix Space pane down.
    /// </summary>
    public static class CodexStreamJsonParser
    {
        private const int ResultPreviewMaxChars = 240;

        public static List<AgentEvent> ParseLine(string line)
        {
            var events = new List<AgentEvent>();
            if (string.IsNullOrWhiteSpace(line))
                return events;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                events.Add(new AgentEvent(AgentEventKind.RawLine) { Text = line });
                return events;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var type = GetString(root, "type");
                switch (type)
                {
                    case "thread.started":
                        events.Add(new AgentEvent(AgentEventKind.SystemInit)
                        {
                            SessionId = GetString(root, "thread_id"),
                        });
                        break;

                    case "item.started":
                        if (TryGetItem(root, out var startedItem))
                            ParseStartedItem(startedItem, events);
                        break;

                    case "item.completed":
                        if (TryGetItem(root, out var completedItem))
                            ParseCompletedItem(completedItem, events);
                        break;

                    case "turn.completed":
                        events.Add(ParseCompletedTurn(root));
                        break;

                    case "turn.failed":
                    case "error":
                        events.Add(new AgentEvent(AgentEventKind.Result)
                        {
                            IsError = true,
                            Subtype = type,
                            Text = ExtractError(root) ?? "Codex turn failed.",
                        });
                        break;

                    case "turn.started":
                        break;

                    default:
                        events.Add(new AgentEvent(AgentEventKind.RawLine) { Text = line, Subtype = type });
                        break;
                }
            }

            return events;
        }

        private static void ParseStartedItem(JsonElement item, List<AgentEvent> events)
        {
            var type = GetString(item, "type");
            if (!IsToolItem(type))
                return;

            events.Add(new AgentEvent(AgentEventKind.ToolUse)
            {
                ToolName = GetToolName(item, type),
            });
        }

        private static void ParseCompletedItem(JsonElement item, List<AgentEvent> events)
        {
            var type = GetString(item, "type");
            switch (type)
            {
                case "agent_message":
                    var text = GetString(item, "text");
                    if (!string.IsNullOrEmpty(text))
                        events.Add(new AgentEvent(AgentEventKind.AssistantText) { Text = text });
                    break;

                case "reasoning":
                    var reasoning = GetString(item, "text");
                    if (string.IsNullOrEmpty(reasoning))
                        reasoning = GetString(item, "summary");
                    if (!string.IsNullOrEmpty(reasoning))
                        events.Add(new AgentEvent(AgentEventKind.AssistantThinking) { Text = reasoning });
                    break;

                default:
                    if (!IsToolItem(type))
                        return;

                    var failed = IsFailedItem(item);
                    events.Add(new AgentEvent(AgentEventKind.ToolResult)
                    {
                        ToolName = GetToolName(item, type),
                        IsError = failed,
                        Text = failed ? ExtractToolResult(item) : null,
                    });
                    break;
            }
        }

        private static AgentEvent ParseCompletedTurn(JsonElement root)
        {
            var evt = new AgentEvent(AgentEventKind.Result);
            if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return evt;

            evt.InputTokens = GetLong(usage, "input_tokens");
            evt.OutputTokens = GetLong(usage, "output_tokens");
            evt.CacheReadTokens = GetLong(usage, "cached_input_tokens");
            evt.CacheCreationTokens = GetLong(usage, "cache_write_input_tokens");
            return evt;
        }

        private static bool TryGetItem(JsonElement root, out JsonElement item)
        {
            item = default;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("item", out item) &&
                   item.ValueKind == JsonValueKind.Object;
        }

        private static bool IsToolItem(string? type)
            => type is "command_execution" or "mcp_tool_call" or "file_change" or "web_search";

        private static string GetToolName(JsonElement item, string? type)
        {
            switch (type)
            {
                case "command_execution":
                    return "shell";
                case "file_change":
                    return "apply_patch";
                case "web_search":
                    return "web_search";
                case "mcp_tool_call":
                    var tool = GetString(item, "tool") ?? GetString(item, "name");
                    var server = GetString(item, "server");
                    return string.IsNullOrEmpty(server) ? tool ?? "mcp" : server + "/" + (tool ?? "tool");
                default:
                    return type ?? "tool";
            }
        }

        private static bool IsFailedItem(JsonElement item)
        {
            var status = GetString(item, "status");
            if (status is "failed" or "error" or "cancelled")
                return true;

            return item.TryGetProperty("exit_code", out var exitCode) &&
                   exitCode.ValueKind == JsonValueKind.Number &&
                   exitCode.TryGetInt32(out var code) && code != 0;
        }

        private static string? ExtractToolResult(JsonElement item)
        {
            var text = GetString(item, "error") ?? GetString(item, "aggregated_output") ??
                       GetString(item, "result");
            return Preview(text);
        }

        private static string? ExtractError(JsonElement root)
        {
            var text = GetString(root, "message") ?? GetString(root, "error");
            if (!string.IsNullOrEmpty(text))
                return text;

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                return GetString(error, "message");
            return null;
        }

        private static string? Preview(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            text = text!.Replace("\r", string.Empty).Trim();
            return text.Length <= ResultPreviewMaxChars
                ? text
                : text.Substring(0, ResultPreviewMaxChars) + "…";
        }

        private static long GetLong(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
                ? result
                : 0L;

        private static string? GetString(JsonElement element, string property)
            => element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
