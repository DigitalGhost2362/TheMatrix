using System.Text.Json;
using Feeder.Matrix.Backends;
using Feeder.Matrix.Health;
using Feeder.Matrix.Unity;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Tools;

/// <summary>
/// Tools served by the matrix itself, listed even while Unity is hung, reloading or gone:
/// <c>editor-health</c> and, unless switched off, the <c>tool-call</c> proxy.
/// </summary>
public sealed class ServerTools(
    UnityLinkRegistry registry,
    McpClientTracker clients,
    EditorHealth health,
    IOptions<MatrixOptions> options,
    ILogger<ServerTools> logger)
{
    public const string ToolCallName = "tool-call";
    const int MaxNameLength = 128;

    static readonly JsonElement EmptySchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{},"additionalProperties":false}""");

    static readonly JsonElement ToolCallSchema = JsonSerializer.Deserialize<JsonElement>("""
        {"type":"object","properties":{
          "name":{"type":"string","description":"Unity tool name, for example 'scene-open'."},
          "arguments":{"type":"object","description":"Arguments of that tool."}},
         "required":["name"],"additionalProperties":false}
        """);

    public IReadOnlyList<Tool> List()
    {
        var tools = new List<Tool>
        {
            new()
            {
                Name = EditorHealth.ToolName,
                Title = "Editor / Health",
                Description = "Says whether the Unity editor is responsive, busy, reloading, stalled or not linked, and what holds it " +
                              "(open editor windows and dialogs, Matrix calls in flight, last log lines). Works while Unity is hung or " +
                              "reloading: call it when a tool times out instead of guessing.",
                InputSchema = EmptySchema,
                Annotations = new ToolAnnotations { ReadOnlyHint = true, IdempotentHint = true },
            },
        };
        if (options.Value.EnableToolCallProxy)
        {
            tools.Add(new Tool
            {
                Name = ToolCallName,
                Title = "Tool / Call",
                Description = "Calls a Unity tool by name when it was just enabled and your tool list does not show it yet. " +
                              "Refused for tools already in your list (call those directly) and for tools your Claude Code " +
                              "permission rules or hooks cover.",
                InputSchema = ToolCallSchema,
            });
        }
        return tools;
    }

    public bool Handles(string? name)
        => name == EditorHealth.ToolName || (name == ToolCallName && options.Value.EnableToolCallProxy);

    public async ValueTask<CallToolResult> CallAsync(CallToolRequestParams request, string? sessionId, IUnityBackend backend, CancellationToken ct)
    {
        if (request.Name == EditorHealth.ToolName)
            return Text(health.Report());
        return await ProxyAsync(request, sessionId, backend, ct);
    }

    async ValueTask<CallToolResult> ProxyAsync(CallToolRequestParams request, string? sessionId, IUnityBackend backend, CancellationToken ct)
    {
        var args = request.Arguments;
        if (args is null || !args.TryGetValue("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            return Refuse("tool-call needs 'name', the Unity tool to call, as a string.");

        IDictionary<string, JsonElement>? targetArguments = null;
        if (args.TryGetValue("arguments", out var argumentsElement) && argumentsElement.ValueKind != JsonValueKind.Null)
        {
            if (argumentsElement.ValueKind != JsonValueKind.Object)
                return Refuse("tool-call 'arguments' must be a JSON object.");
            targetArguments = argumentsElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }

        var link = registry.GetLatestLink();
        var name = StripPrefix(nameElement.GetString()!.Trim());
        if (name.Length == 0 || name.Length > MaxNameLength)
            return Refuse($"tool-call needs a tool name of 1 to {MaxNameLength} characters.");

        // 1. never itself or another matrix tool
        if (string.Equals(name, ToolCallName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, EditorHealth.ToolName, StringComparison.OrdinalIgnoreCase))
            return Refuse($"'{name}' is a matrix tool: call it directly, not through tool-call.");

        // one spelling for every check below and for the call itself
        name = link?.Registry?.Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Name ?? name;
        var entries = EntryNames(link);
        var targets = entries.Select(entry => ClaudePermissionMirror.FullName(entry, name)).Distinct().ToArray();

        // 2. only for the gap before the client re-lists its tools
        if (clients.WasListed(sessionId, name))
            return Refuse($"'{name}' is already in your tool list: call {targets[0]} directly so your client's permission rules apply.");

        // 4-6. Claude Code deny/ask rules and PreToolUse hooks, failing closed on unreadable settings
        var proxies = entries.Select(entry => ClaudePermissionMirror.FullName(entry, ToolCallName)).Distinct().ToArray();
        var refusal = ClaudePermissionMirror.Check(targets, proxies, ClaudePermissionMirror.DiscoverSources(link?.ProjectRoot));
        if (refusal is not null)
        {
            logger.LogInformation("tool-call {Tool} refused for session {SessionId}: {Reason}", name, sessionId, refusal);
            return Refuse(refusal);
        }

        // 3. disabled or unknown tools are refused by Unity itself (FMP_TOOL_NOT_FOUND)
        logger.LogInformation("tool-call {Tool} for session {SessionId}", name, sessionId);
        CallToolResult result;
        try
        {
            result = await backend.CallToolAsync(new CallToolRequestParams { Name = name, Arguments = targetArguments }, ct);
        }
        catch (McpProtocolException e)
        {
            return new CallToolResult { IsError = true, Content = [Block($"via tool-call -> {name}"), Block(e.Message)] };
        }

        var content = new List<ContentBlock> { Block($"via tool-call -> {name}") };
        content.AddRange(result.Content ?? []);
        return new CallToolResult { IsError = result.IsError, Content = content, StructuredContent = result.StructuredContent };
    }

    // agents sometimes pass the client-side name, mcp__<entry>__<tool>
    static string StripPrefix(string name)
    {
        if (!name.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
            return name;
        var split = name.IndexOf("__", 5, StringComparison.Ordinal);
        return split < 0 ? name : name[(split + 2)..];
    }

    /// <summary>
    /// Every name the agents may know this matrix by: the one Unity reports, plus every MCP entry that points
    /// at this port in the project's <c>.mcp.json</c> and in Claude Code's own <c>.claude.json</c> (user and
    /// local scope entries).
    /// </summary>
    IReadOnlyList<string> EntryNames(UnityLink? link)
    {
        var names = new List<string> { string.IsNullOrWhiteSpace(link?.McpEntryName) ? "Feeder-MCP" : link!.McpEntryName! };
        var configs = new List<string>();
        if (!string.IsNullOrEmpty(link?.ProjectRoot))
            configs.Add(Path.Combine(link.ProjectRoot, ".mcp.json"));
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDir))
            configs.Add(Path.Combine(configDir, ".claude.json"));
        configs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"));
        foreach (var path in configs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                CollectEntries(document.RootElement, options.Value.Port, names, depth: 0);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Could not read MCP entries from {Path}", path);
            }
        }
        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    // "mcpServers" sits at the top of .mcp.json and of .claude.json, and under each project in .claude.json
    internal static void CollectEntries(JsonElement element, int port, List<string> names, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > 3)
            return;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name == "mcpServers" && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var server in property.Value.EnumerateObject())
                {
                    var raw = server.Value.GetRawText();
                    if (raw.Contains($":{port}/", StringComparison.Ordinal) || raw.Contains($"port={port}", StringComparison.Ordinal))
                        names.Add(server.Name);
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                CollectEntries(property.Value, port, names, depth + 1);
            }
        }
    }

    static CallToolResult Refuse(string message) => new() { IsError = true, Content = [Block(message)] };

    static CallToolResult Text(string text) => new() { Content = [Block(text)] };

    static TextContentBlock Block(string text) => new() { Text = text };
}
