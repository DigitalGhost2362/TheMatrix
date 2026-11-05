namespace Feeder.Matrix.Protocol;

public static class FmpConstants
{
    public const string ProtocolVersion = "1";

    /// <summary>SignalR hub path on the matrix (loopback only).</summary>
    public const string HubPath = "/fmp";

    /// <summary>MCP Streamable HTTP path on the matrix.</summary>
    public const string McpPath = "/mcp";

    public const int DefaultPort = 20270;
    public const int DefaultQueueCapacity = 32;
    public const int HeartbeatIntervalSeconds = 5;
    public const int LinkResumeTtlSeconds = 120;
    public const string DefaultCompileAssemblyName = "DynamicAssembly";
}

/// <summary>Optional matrix features listed in <see cref="UnityRegisterResponse.Capabilities"/>.</summary>
public static class FmpCapabilities
{
    /// <summary>The hub serves <c>Compile</c> to a registered Unity link.</summary>
    public const string Compile = "compile";

    /// <summary>The matrix keeps the editor lock: the hub serves <c>ReleaseEditorLock</c> and pushes <c>EditorLockChanged</c>.</summary>
    public const string EditorLock = "editor-lock";

    /// <summary>The editor lock keeps a queue: the hub serves <c>RemoveFromEditorLockQueue</c> and <c>SetEditorLockQueuePaused</c>, and the lock state lists the waiting sessions.</summary>
    public const string EditorLockQueue = "editor-lock-queue";
}

/// <summary>Editor state reported in handshake and heartbeats.</summary>
public enum FmpEditorState
{
    Ready = 0,
    Compiling = 1,
    Reloading = 2,
    Playing = 3,
    Paused = 4,
}

/// <summary>Flags in <see cref="HeartbeatMessage.Phase"/>: what the editor is doing besides its <see cref="FmpEditorState"/>.</summary>
public static class FmpPhases
{
    public const int None = 0;

    /// <summary>A test run owns the editor.</summary>
    public const int RunningTests = 1;

    /// <summary>Entering or leaving play mode.</summary>
    public const int PlaymodeTransition = 2;
}

/// <summary>Stable FMP error codes (see protocol spec §4).</summary>
public static class FmpErrorCodes
{
    public const string VersionUnsupported = "FMP_VERSION_UNSUPPORTED";
    public const string AuthFailed = "FMP_AUTH_FAILED";
    public const string QueueFull = "FMP_QUEUE_FULL";
    public const string DeadlineExceeded = "FMP_DEADLINE_EXCEEDED";
    public const string Cancelled = "FMP_CANCELLED";
    public const string ToolNotFound = "FMP_TOOL_NOT_FOUND";
    public const string ToolFailed = "FMP_TOOL_FAILED";
    public const string Interrupted = "FMP_INTERRUPTED";
    public const string UnityUnavailable = "FMP_UNITY_UNAVAILABLE";
    public const string Internal = "FMP_INTERNAL";
}
