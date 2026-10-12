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
    public const string UnityUnavailable = "FMP_UNITY_UNAVAILABLE";
    public const string Internal = "FMP_INTERNAL";
}
