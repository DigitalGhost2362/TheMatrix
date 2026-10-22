#nullable enable
using System;

namespace Feeder.MCP.Editor.Matrix
{
    // JSON wire DTOs for FMP/1. These intentionally do not reference the matrix's net10 assembly;
    // SignalR's JSON protocol matches the public property names on both sides.
    [Serializable]
    internal sealed class FmpError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool Retriable { get; set; }
        public byte[]? DetailsJsonUtf8 { get; set; }
    }

    [Serializable]
    internal sealed class UnityRegisterRequest
    {
        public string ProtocolVersion { get; set; } = "1";
        public string ProjectId { get; set; } = string.Empty;
        public string UnityInstanceId { get; set; } = string.Empty;
        public string ProjectPathHash { get; set; } = string.Empty;
        public string UnityVersion { get; set; } = string.Empty;
        public string PluginVersion { get; set; } = string.Empty;
        public string[] SupportedProtocolVersions { get; set; } = Array.Empty<string>();
        public string ToolRegistryHash { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;
        public FmpEditorState EditorState { get; set; }
        public string[] PendingOperations { get; set; } = Array.Empty<string>();
        public string? ProjectRoot { get; set; }
        public string? McpEntryName { get; set; }
        public string[] Features { get; set; } = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class UnityRegisterResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public string ChosenProtocolVersion { get; set; } = string.Empty;
        public bool RegistryUpToDate { get; set; }
        public string[] AwaitedOperations { get; set; } = Array.Empty<string>();
        public int QueueCapacity { get; set; }
        public int HeartbeatIntervalSeconds { get; set; }
        public FmpError? Error { get; set; }
        public string[]? Capabilities { get; set; }
    }

    [Serializable]
    internal sealed class CompileRequest
    {
        public string Source { get; set; } = string.Empty;
        public string[] ReferencePaths { get; set; } = Array.Empty<string>();
        public string AssemblyName { get; set; } = "DynamicAssembly";
        public bool SyntaxOnly { get; set; }
        public int TimeoutMs { get; set; }
    }

    [Serializable]
    internal sealed class CompileResponse
    {
        public bool Success { get; set; }
        public byte[]? Assembly { get; set; }
        public string[]? Diagnostics { get; set; }
        public string? Error { get; set; }
    }

    [Serializable]
    internal sealed class ToolDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public byte[] InputSchemaJsonUtf8 { get; set; } = Array.Empty<byte>();
        public bool ReadOnly { get; set; }
        public bool Destructive { get; set; }
        public bool Idempotent { get; set; }
        public bool ThreadSafe { get; set; }
    }

    [Serializable]
    internal sealed class RegistrySnapshot
    {
        public string ProjectId { get; set; } = string.Empty;
        public string RegistryHash { get; set; } = string.Empty;
        public ToolDescriptor[] Tools { get; set; } = Array.Empty<ToolDescriptor>();
    }

    [Serializable]
    internal sealed class ToolInvoke
    {
        public string OperationId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public byte[] ArgumentsJsonUtf8 { get; set; } = Array.Empty<byte>();
        public long DeadlineUnixMs { get; set; }
        public string? TraceId { get; set; }
    }

    [Serializable]
    internal sealed class ToolResult
    {
        public string OperationId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public byte[]? ContentJsonUtf8 { get; set; }
        public FmpError? Error { get; set; }
    }

    [Serializable]
    internal sealed class McpClientInfo
    {
        public string SessionId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientTitle { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
    }

    /// <summary>Who holds the editor lock; kept by the matrix and pushed with EditorLockChanged.</summary>
    [Serializable]
    internal sealed class EditorLockState
    {
        public bool Held { get; set; }
        // "agent" or "outside"
        public string Kind { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Task { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public long AcquiredUnixMs { get; set; }
        public long LastActivityUnixMs { get; set; }
        public long ExpiresUnixMs { get; set; }
        public int CallsInFlight { get; set; }
    }

    [Serializable]
    internal sealed class HeartbeatMessage
    {
        public string UnityInstanceId { get; set; } = string.Empty;
        public FmpEditorState EditorState { get; set; }
        public long LastMainTickUnixMs { get; set; }
        public bool Updating { get; set; }
        public bool Focused { get; set; }
        public InFlightOperation[] InFlight { get; set; } = Array.Empty<InFlightOperation>();
        public int ProcessId { get; set; }
        public string? ConsoleLogPath { get; set; }
    }

    [Serializable]
    internal sealed class InFlightOperation
    {
        public string OperationId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public long StartedUnixMs { get; set; }
    }

    [Serializable]
    internal sealed class ToolProgress
    {
        public string OperationId { get; set; } = string.Empty;
        public double? Progress { get; set; }
        public string? Message { get; set; }
        public bool Deferred { get; set; }
        public long ExtendDeadlineMs { get; set; }
    }

    internal enum FmpEditorState
    {
        Ready,
        Compiling,
        Reloading,
        Playing,
        Paused,
    }
}
