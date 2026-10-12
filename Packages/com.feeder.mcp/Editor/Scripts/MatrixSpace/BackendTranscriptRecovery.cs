#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Feeder.Json;
using UnityEngine;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>Best-effort one-time recovery for panes whose pre-persistence transcript was lost.</summary>
    internal static class BackendTranscriptRecovery
    {
        public static bool TryRecover(string backendId, string sessionId,
            out List<MatrixSpaceSessionStore.TranscriptSnapshot> recovered)
        {
            recovered = new List<MatrixSpaceSessionStore.TranscriptSnapshot>();
            try
            {
                var file = FindSessionFile(backendId, sessionId);
                if (file == null)
                    return false;

                var entries = backendId == "codex"
                    ? ReadCodex(file)
                    : ReadClaude(file);

                foreach (var entry in entries)
                {
                    var snapshot = new MatrixSpaceSessionStore.TranscriptSnapshot();
                    snapshot.Capture(entry);
                    recovered.Add(snapshot);
                }

                if (recovered.Count > 0)
                    Debug.Log($"[MatrixSpaceRecovery] Restored {recovered.Count} transcript entries for {backendId} session {sessionId}.");
                return recovered.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatrixSpaceRecovery] Could not recover {backendId} session {sessionId}: {ex.Message}");
                recovered.Clear();
                return false;
            }
        }

        private static string? FindSessionFile(string backendId, string sessionId)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var root = backendId == "codex"
                ? Path.Combine(home, ".codex", "sessions")
                : Path.Combine(home, ".claude", "projects");
            if (!Directory.Exists(root))
                return null;

            return Directory.EnumerateFiles(root, $"*{sessionId}*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        internal static List<TranscriptEntry> ReadCodex(string path)
        {
            var result = new List<TranscriptEntry>();
            foreach (var line in File.ReadLines(path))
            {
                if (!TryParse(line, out var document))
                    continue;
                using (document)
                {
                    var root = document.RootElement;
                    if (!StringPropertyEquals(root, "type", "response_item") ||
                        !root.TryGetProperty("payload", out var payload))
                        continue;

                    var timestamp = ReadTimestamp(root);
                    if (StringPropertyEquals(payload, "type", "message") &&
                        payload.TryGetProperty("role", out var roleElement))
                    {
                        var role = roleElement.GetString();
                        var kind = role == "user" ? TranscriptEntryKind.User : TranscriptEntryKind.Assistant;
                        if (role != "user" && role != "assistant")
                            continue;

                        foreach (var text in ReadContentTexts(payload, role == "user" ? "input_text" : "output_text"))
                            AddDistinct(result, NewEntry(kind, text, timestamp));
                    }
                    else if (StringPropertyEquals(payload, "type", "reasoning") &&
                             payload.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in summary.EnumerateArray())
                        {
                            if (item.TryGetProperty("text", out var text))
                                AddDistinct(result, NewEntry(TranscriptEntryKind.Thinking, text.GetString(), timestamp));
                        }
                    }
                    else if (StringPropertyEquals(payload, "type", "custom_tool_call") &&
                             payload.TryGetProperty("name", out var name))
                    {
                        var toolName = name.GetString() ?? "unknown";
                        AddDistinct(result, new TranscriptEntry(TranscriptEntryKind.ToolUse,
                            $"> executing: {toolName}", toolName) { Timestamp = timestamp });
                    }
                }
            }
            return result;
        }

        internal static List<TranscriptEntry> ReadClaude(string path)
        {
            var result = new List<TranscriptEntry>();
            foreach (var line in File.ReadLines(path))
            {
                if (!TryParse(line, out var document))
                    continue;
                using (document)
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var typeElement))
                        continue;
                    var type = typeElement.GetString();
                    if (type != "user" && type != "assistant")
                        continue;
                    if (type == "user" && root.TryGetProperty("toolUseResult", out _))
                        continue;
                    if (!root.TryGetProperty("message", out var message) ||
                        !message.TryGetProperty("content", out var content))
                        continue;

                    var timestamp = ReadTimestamp(root);
                    if (content.ValueKind == JsonValueKind.String && type == "user")
                    {
                        AddDistinct(result, NewEntry(TranscriptEntryKind.User, content.GetString(), timestamp));
                        continue;
                    }
                    if (content.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var block in content.EnumerateArray())
                    {
                        if (!block.TryGetProperty("type", out var blockTypeElement))
                            continue;
                        var blockType = blockTypeElement.GetString();
                        if (blockType == "text" && block.TryGetProperty("text", out var text))
                        {
                            AddDistinct(result, NewEntry(type == "user" ? TranscriptEntryKind.User : TranscriptEntryKind.Assistant,
                                text.GetString(), timestamp));
                        }
                        else if (type == "assistant" && blockType == "thinking" && block.TryGetProperty("thinking", out var thinking))
                        {
                            AddDistinct(result, NewEntry(TranscriptEntryKind.Thinking, thinking.GetString(), timestamp));
                        }
                        else if (type == "assistant" && blockType == "tool_use" && block.TryGetProperty("name", out var name))
                        {
                            var toolName = name.GetString() ?? "unknown";
                            AddDistinct(result, new TranscriptEntry(TranscriptEntryKind.ToolUse,
                                $"> executing: {toolName}", toolName) { Timestamp = timestamp });
                        }
                    }
                }
            }
            return result;
        }

        private static IEnumerable<string> ReadContentTexts(JsonElement payload, string expectedBlockType)
        {
            if (!payload.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                yield break;
            foreach (var block in content.EnumerateArray())
            {
                if (!StringPropertyEquals(block, "type", expectedBlockType) || !block.TryGetProperty("text", out var text))
                    continue;
                var value = text.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value!;
            }
        }

        private static TranscriptEntry NewEntry(TranscriptEntryKind kind, string? text, DateTime timestamp)
            => new(kind, IsImportedEnvelope(text) ? string.Empty : text ?? string.Empty) { Timestamp = timestamp };

        internal static bool IsImportedEnvelope(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.TrimStart();
            return trimmed.StartsWith("<recommended_plugins>", StringComparison.Ordinal) ||
                   trimmed.StartsWith("<environment_context>", StringComparison.Ordinal) ||
                   trimmed.StartsWith("<permissions instructions>", StringComparison.Ordinal) ||
                   trimmed.StartsWith("<app-context>", StringComparison.Ordinal) ||
                   trimmed.StartsWith("<collaboration_mode>", StringComparison.Ordinal) ||
                   trimmed.StartsWith("<apps_instructions>", StringComparison.Ordinal) ||
                   trimmed.StartsWith("<plugins_instructions>", StringComparison.Ordinal) ||
                   trimmed.StartsWith("<skills_instructions>", StringComparison.Ordinal) ||
                   trimmed.StartsWith("<multi_agent_mode>", StringComparison.Ordinal);
        }

        private static void AddDistinct(List<TranscriptEntry> result, TranscriptEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Text))
                return;
            var previous = result.Count > 0 ? result[^1] : null;
            if (previous != null && previous.Kind == entry.Kind && previous.ToolName == entry.ToolName && previous.Text == entry.Text)
                return;
            result.Add(entry);
        }

        private static DateTime ReadTimestamp(JsonElement root)
        {
            if (root.TryGetProperty("timestamp", out var timestamp) &&
                DateTimeOffset.TryParse(timestamp.GetString(), out var parsed))
                return parsed.LocalDateTime;
            return DateTime.Now;
        }

        private static bool StringPropertyEquals(JsonElement element, string propertyName, string expected)
            => element.TryGetProperty(propertyName, out var property) && property.GetString() == expected;

        private static bool TryParse(string line, out JsonDocument document)
        {
            try
            {
                document = JsonDocument.Parse(line);
                return true;
            }
            catch (JsonException)
            {
                document = null!;
                return false;
            }
        }
    }
}
