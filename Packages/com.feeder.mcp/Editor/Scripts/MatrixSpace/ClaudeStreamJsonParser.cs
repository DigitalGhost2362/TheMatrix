#nullable enable

using System;
using System.Collections.Generic;
using Feeder.Json;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>
    /// Parses one stdout line of `claude -p --output-format stream-json --verbose` into
    /// <see cref="AgentEvent"/>s. Lenient by design: anything unrecognized becomes a
    /// <see cref="AgentEventKind.RawLine"/> event so a CLI version drift never crashes a pane.
    /// </summary>
    public static class ClaudeStreamJsonParser
    {
        private const int ToolResultPreviewMaxChars = 200;

        /// <summary>
        /// Per-turn parser state. With `--include-partial-messages` the CLI streams token
        /// deltas AND still emits the assembled assistant message afterwards; this tracks
        /// whether the current message was already streamed so the full message is not
        /// appended a second time.
        /// </summary>
        public sealed class State
        {
            public bool SawDeltaForCurrentMessage;
        }

        public static List<AgentEvent> ParseLine(string line, State? state = null)
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
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var typeProp))
                {
                    events.Add(new AgentEvent(AgentEventKind.RawLine) { Text = line });
                    return events;
                }

                switch (typeProp.GetString())
                {
                    case "system":
                        ParseSystem(root, events, line);
                        break;
                    case "assistant":
                        ParseAssistant(root, events, state);
                        break;
                    case "stream_event":
                        ParseStreamEvent(root, events, state);
                        break;
                    case "user":
                        ParseUser(root, events);
                        break;
                    case "result":
                        ParseResult(root, events);
                        break;
                    default:
                        events.Add(new AgentEvent(AgentEventKind.RawLine) { Text = line });
                        break;
                }
            }

            return events;
        }

        private static void ParseSystem(JsonElement root, List<AgentEvent> events, string line)
        {
            var subtype = GetString(root, "subtype");
            if (subtype == "init")
            {
                events.Add(new AgentEvent(AgentEventKind.SystemInit)
                {
                    Subtype = subtype,
                    SessionId = GetString(root, "session_id"),
                });
            }
            else
            {
                events.Add(new AgentEvent(AgentEventKind.RawLine) { Subtype = subtype, Text = line });
            }
        }

        private static void ParseAssistant(JsonElement root, List<AgentEvent> events, State? state)
        {
            if (!TryGetContent(root, out var content))
                return;

            // Already delivered token-by-token via stream_events for this message; the
            // assembled message would duplicate everything.
            var streamed = state?.SawDeltaForCurrentMessage == true;

            foreach (var block in content.EnumerateArray())
            {
                switch (GetString(block, "type"))
                {
                    case "text":
                        var text = GetString(block, "text");
                        if (!streamed && !string.IsNullOrEmpty(text))
                            events.Add(new AgentEvent(AgentEventKind.AssistantText) { Text = text });
                        break;
                    case "thinking":
                        var thinking = GetString(block, "thinking");
                        if (!streamed && !string.IsNullOrEmpty(thinking))
                            events.Add(new AgentEvent(AgentEventKind.AssistantThinking) { Text = thinking });
                        break;
                    case "tool_use":
                        if (!streamed)
                        {
                            events.Add(new AgentEvent(AgentEventKind.ToolUse)
                            {
                                ToolName = GetString(block, "name"),
                            });
                        }
                        break;
                }
            }
        }

        private static void ParseStreamEvent(JsonElement root, List<AgentEvent> events, State? state)
        {
            if (!root.TryGetProperty("event", out var evt) || evt.ValueKind != JsonValueKind.Object)
                return;

            switch (GetString(evt, "type"))
            {
                case "message_start":
                    if (state != null)
                        state.SawDeltaForCurrentMessage = false;
                    break;

                case "content_block_start":
                    // Surface tool activity the moment the block starts instead of
                    // waiting for the assembled assistant message.
                    if (evt.TryGetProperty("content_block", out var block) &&
                        block.ValueKind == JsonValueKind.Object &&
                        GetString(block, "type") == "tool_use")
                    {
                        if (state != null)
                            state.SawDeltaForCurrentMessage = true;
                        events.Add(new AgentEvent(AgentEventKind.ToolUse)
                        {
                            ToolName = GetString(block, "name"),
                        });
                    }
                    break;

                case "content_block_delta":
                    if (!evt.TryGetProperty("delta", out var delta) ||
                        delta.ValueKind != JsonValueKind.Object)
                    {
                        break;
                    }
                    switch (GetString(delta, "type"))
                    {
                        case "text_delta":
                            var text = GetString(delta, "text");
                            if (!string.IsNullOrEmpty(text))
                            {
                                if (state != null)
                                    state.SawDeltaForCurrentMessage = true;
                                events.Add(new AgentEvent(AgentEventKind.AssistantText)
                                {
                                    Text = text,
                                    IsDelta = true,
                                });
                            }
                            break;
                        case "thinking_delta":
                            var thinking = GetString(delta, "thinking");
                            if (!string.IsNullOrEmpty(thinking))
                            {
                                if (state != null)
                                    state.SawDeltaForCurrentMessage = true;
                                events.Add(new AgentEvent(AgentEventKind.AssistantThinking)
                                {
                                    Text = thinking,
                                    IsDelta = true,
                                });
                            }
                            break;
                    }
                    break;
            }
        }

        private static void ParseUser(JsonElement root, List<AgentEvent> events)
        {
            if (!TryGetContent(root, out var content))
                return;

            foreach (var block in content.EnumerateArray())
            {
                if (GetString(block, "type") != "tool_result")
                    continue;

                var isError = block.TryGetProperty("is_error", out var errProp) &&
                              errProp.ValueKind == JsonValueKind.True;
                events.Add(new AgentEvent(AgentEventKind.ToolResult)
                {
                    IsError = isError,
                    Text = ExtractToolResultPreview(block),
                });
            }
        }

        private static void ParseResult(JsonElement root, List<AgentEvent> events)
        {
            var evt = new AgentEvent(AgentEventKind.Result)
            {
                Subtype = GetString(root, "subtype"),
                SessionId = GetString(root, "session_id"),
                Text = GetString(root, "result"),
            };

            // Failed turns often carry the real reason only in an "errors" array
            // (e.g. "No conversation found with session ID: …").
            if (string.IsNullOrEmpty(evt.Text) &&
                root.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var err in errors.EnumerateArray())
                {
                    if (err.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(err.GetString()))
                        parts.Add(err.GetString()!);
                }
                if (parts.Count > 0)
                    evt.Text = string.Join("\n", parts);
            }

            if (root.TryGetProperty("is_error", out var isErrProp))
                evt.IsError = isErrProp.ValueKind == JsonValueKind.True;
            else
                evt.IsError = evt.Subtype != null && evt.Subtype.StartsWith("error", StringComparison.Ordinal);

            if (root.TryGetProperty("total_cost_usd", out var costProp) &&
                costProp.ValueKind == JsonValueKind.Number)
            {
                evt.CostUsd = costProp.GetDecimal();
            }

            if (root.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
            {
                evt.InputTokens = GetLong(usage, "input_tokens");
                evt.OutputTokens = GetLong(usage, "output_tokens");
                evt.CacheReadTokens = GetLong(usage, "cache_read_input_tokens");
                evt.CacheCreationTokens = GetLong(usage, "cache_creation_input_tokens");
            }

            events.Add(evt);
        }

        private static long GetLong(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var prop) &&
                   prop.ValueKind == JsonValueKind.Number
                ? prop.GetInt64()
                : 0L;
        }

        private static bool TryGetContent(JsonElement root, out JsonElement content)
        {
            content = default;
            return root.TryGetProperty("message", out var message) &&
                   message.ValueKind == JsonValueKind.Object &&
                   message.TryGetProperty("content", out content) &&
                   content.ValueKind == JsonValueKind.Array;
        }

        private static string? ExtractToolResultPreview(JsonElement toolResultBlock)
        {
            if (!toolResultBlock.TryGetProperty("content", out var content))
                return null;

            string? text = null;
            if (content.ValueKind == JsonValueKind.String)
            {
                text = content.GetString();
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (GetString(block, "type") == "text")
                    {
                        text = GetString(block, "text");
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(text))
                return null;

            text = text!.Replace("\r", "").Replace("\n", " ");
            return text.Length <= ToolResultPreviewMaxChars
                ? text
                : text.Substring(0, ToolResultPreviewMaxChars) + "…";
        }

        private static string? GetString(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(property, out var prop) &&
                   prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }
    }
}
