using MessagePack;

namespace Feeder.Matrix.Protocol;

// FMP/1 message DTOs. Integer keys keep MessagePack frames compact; only append keys - never
// renumber - within protocol v1 (unknown keys are ignored by both sides).

[MessagePackObject]
public sealed record FmpError
{
    [Key(0)] public required string Code { get; init; }
    [Key(1)] public required string Message { get; init; }
    [Key(2)] public bool Retriable { get; init; }
    [Key(3)] public byte[]? DetailsJsonUtf8 { get; init; }
}

[MessagePackObject]
public sealed record UnityRegisterRequest
{
    [Key(0)] public required string ProtocolVersion { get; init; }
    [Key(1)] public required string ProjectId { get; init; }
    [Key(2)] public required string UnityInstanceId { get; init; }
    [Key(3)] public required string ProjectPathHash { get; init; }
    [Key(4)] public required string UnityVersion { get; init; }
    [Key(5)] public required string PluginVersion { get; init; }
    [Key(6)] public required string[] SupportedProtocolVersions { get; init; }
    [Key(7)] public required string ToolRegistryHash { get; init; }
    [Key(8)] public required string AuthToken { get; init; }
    [Key(9)] public FmpEditorState EditorState { get; init; }
    [Key(10)] public string[] PendingOperations { get; set; } = [];
    // Project folder, so the matrix can read the project's agent settings next to it.
    [Key(11)] public string? ProjectRoot { get; init; }
    // Name of the matrix's entry in the agents' MCP config (tool names are mcp__<entry>__<tool>).
    [Key(12)] public string? McpEntryName { get; init; }
    // Optional editor features this plugin handles, e.g. "editor-lock" (it accepts EditorLockChanged).
    [Key(13)] public string[] Features { get; set; } = [];
}

[MessagePackObject]
public sealed record UnityRegisterResponse
{
    [Key(0)] public required string SessionId { get; init; }
    [Key(1)] public required string ChosenProtocolVersion { get; init; }
    [Key(2)] public bool RegistryUpToDate { get; init; }
    [Key(3)] public string[] AwaitedOperations { get; set; } = [];
    [Key(4)] public int QueueCapacity { get; set; } = FmpConstants.DefaultQueueCapacity;
    [Key(5)] public int HeartbeatIntervalSeconds { get; set; } = FmpConstants.HeartbeatIntervalSeconds;
    [Key(6)] public FmpError? Error { get; init; }
    [Key(7)] public string[] Capabilities { get; set; } = [];
}

[MessagePackObject]
public sealed record ToolDescriptor
{
    [Key(0)] public required string Name { get; init; }
    [Key(1)] public string? Title { get; init; }
    [Key(2)] public string? Description { get; init; }
    [Key(3)] public required byte[] InputSchemaJsonUtf8 { get; init; }
    [Key(4)] public bool ReadOnly { get; init; }
    [Key(5)] public bool Destructive { get; init; }
    [Key(6)] public bool Idempotent { get; init; }
    [Key(7)] public bool ThreadSafe { get; init; }
}

[MessagePackObject]
public sealed record RegistrySnapshot
{
    [Key(0)] public required string ProjectId { get; init; }
    [Key(1)] public required string RegistryHash { get; init; }
    [Key(2)] public ToolDescriptor[] Tools { get; set; } = [];
    // Prompts/resources descriptors follow the same pattern when the plugin exposes them (Phase 6).
}

[MessagePackObject]
public sealed record ToolInvoke
{
    [Key(0)] public required string OperationId { get; init; }
    [Key(1)] public required string ToolName { get; init; }
    [Key(2)] public required byte[] ArgumentsJsonUtf8 { get; init; }
    [Key(3)] public long DeadlineUnixMs { get; init; }
    [Key(4)] public string? TraceId { get; init; }
}

[MessagePackObject]
public sealed record ToolResult
{
    [Key(0)] public required string OperationId { get; init; }
    [Key(1)] public bool Success { get; init; }
    [Key(2)] public byte[]? ContentJsonUtf8 { get; init; }
    [Key(3)] public FmpError? Error { get; init; }
}

[MessagePackObject]
public sealed record ToolProgress
{
    [Key(0)] public required string OperationId { get; init; }
    [Key(1)] public double? Progress { get; init; }
    [Key(2)] public string? Message { get; init; }
    // The tool answered Processing and completes later (after a compile or domain reload).
    [Key(3)] public bool Deferred { get; init; }
    // The tool asks for this much more time, counted from now (capped by the matrix).
    [Key(4)] public long ExtendDeadlineMs { get; init; }
}

[MessagePackObject]
public sealed record HeartbeatMessage
{
    [Key(0)] public required string UnityInstanceId { get; init; }
    [Key(1)] public FmpEditorState EditorState { get; init; }
    // Sent from a background thread, so the fields below still arrive while the main thread is held.
    [Key(2)] public long LastMainTickUnixMs { get; init; }
    [Key(3)] public bool Updating { get; init; }
    [Key(4)] public bool Focused { get; init; }
    [Key(5)] public InFlightOperation[] InFlight { get; set; } = [];
    [Key(6)] public int ProcessId { get; init; }
    [Key(7)] public string? ConsoleLogPath { get; init; }
}

[MessagePackObject]
public sealed record InFlightOperation
{
    [Key(0)] public string OperationId { get; set; } = string.Empty;
    [Key(1)] public string ToolName { get; set; } = string.Empty;
    [Key(2)] public long StartedUnixMs { get; init; }
}

[MessagePackObject]
public sealed record McpClientInfo
{
    [Key(0)] public required string SessionId { get; init; }
    [Key(1)] public string ClientName { get; set; } = string.Empty;
    [Key(2)] public string ClientTitle { get; set; } = string.Empty;
    [Key(3)] public string ClientVersion { get; set; } = string.Empty;
    [Key(4)] public bool IsConnected { get; init; }
}

[MessagePackObject]
public sealed record CompileRequest
{
    [Key(0)] public required string Source { get; init; }
    [Key(1)] public string[] ReferencePaths { get; set; } = [];
    [Key(2)] public string AssemblyName { get; set; } = FmpConstants.DefaultCompileAssemblyName;
    [Key(3)] public bool SyntaxOnly { get; init; }
    [Key(4)] public int TimeoutMs { get; init; }
}

[MessagePackObject]
public sealed record CompileResponse
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public byte[]? Assembly { get; init; }
    [Key(2)] public string[] Diagnostics { get; set; } = [];
    [Key(3)] public string? Error { get; init; }
}

/// <summary>Who holds the editor lock, pushed to the editor with <c>EditorLockChanged</c>.</summary>
[MessagePackObject]
public sealed record EditorLockState
{
    [Key(0)] public bool Held { get; init; }
    // "agent" (an MCP session) or "outside" (a lock file written by a session that does not claim through the matrix)
    [Key(1)] public string Kind { get; set; } = string.Empty;
    [Key(2)] public string Owner { get; set; } = string.Empty;
    [Key(3)] public string Task { get; set; } = string.Empty;
    [Key(4)] public string ClientName { get; set; } = string.Empty;
    [Key(5)] public string ClientVersion { get; set; } = string.Empty;
    [Key(6)] public string SessionId { get; set; } = string.Empty;
    [Key(7)] public long AcquiredUnixMs { get; init; }
    [Key(8)] public long LastActivityUnixMs { get; init; }
    // when the matrix frees an idle claim on its own; 0 = never
    [Key(9)] public long ExpiresUnixMs { get; init; }
    [Key(10)] public int CallsInFlight { get; init; }
}
