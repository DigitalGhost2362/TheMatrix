namespace Feeder.Matrix;

public sealed class MatrixOptions
{
    public const string SectionName = "FeederMatrix";

    /// <summary>Loopback port for both the MCP endpoint and the FMP hub.</summary>
    public int Port { get; set; } = Protocol.FmpConstants.DefaultPort;

    /// <summary>When true and no Unity editor is linked, tool calls are served by the in-process mock backend (tests, matrix-only development).</summary>
    public bool EnableMockBackend { get; set; }

    /// <summary>Default per-request timeout when the client sets no deadline.</summary>
    public TimeSpan DefaultToolTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public long MaxRequestBodyBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Require a bearer token on MCP requests and the same token in the Unity FMP handshake.</summary>
    public bool RequireAuth { get; set; }

    /// <summary>Per-project token supplied by the Unity package when authentication is enabled.</summary>
    public string? Token { get; set; }

    /// <summary>Script compiles that may run at once; further requests wait in a bounded queue.</summary>
    public int CompileConcurrency { get; set; } = 2;

    /// <summary>Script compiles that may wait for a free slot before new ones are refused.</summary>
    public int CompileQueueCapacity { get; set; } = 16;

    /// <summary>Compile deadline (queueing included) when the request sets no timeout.</summary>
    public TimeSpan CompileTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public int CompileMaxSourceChars { get; set; } = 5 * 1024 * 1024;

    public int CompileMaxReferences { get; set; } = 2000;
}
