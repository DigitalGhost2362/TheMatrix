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

    /// <summary>How long a tool call waits for a Unity editor that dropped its link (domain reload) to come back.</summary>
    public TimeSpan ReconnectGrace { get; set; } = TimeSpan.FromSeconds(180);

    /// <summary>Deadline of a tool that answered Processing and completes after a compile or domain reload.</summary>
    public TimeSpan DeferredToolTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Upper bound for any tool call, including the extra time a tool asks for.</summary>
    public TimeSpan MaxToolTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Lists the <c>tool-call</c> proxy tool. Off removes it from the tool list.</summary>
    public bool EnableToolCallProxy { get; set; } = true;

    /// <summary>While a session or a person holds the editor lock, refuse other sessions' editor-changing tools. Off only reports the holder.</summary>
    public bool EnforceEditorLock { get; set; } = true;

    /// <summary>
    /// An editor-changing tool from a session without a lock claims the editor itself when it is free, or waits in the queue
    /// when it is taken. Off (<c>FeederMatrix:ImplicitLock=false</c>): such a call runs while nobody holds the lock and is refused while someone does.
    /// </summary>
    public bool ImplicitLock { get; set; } = true;

    /// <summary>An implicit claim with no tool call for this long is freed.</summary>
    public TimeSpan EditorLockImplicitIdle { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>An implicit claim older than this gives way between two calls while other sessions wait, except in the play session its holder started.</summary>
    public TimeSpan EditorLockImplicitMaxAge { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>An explicit claim older than this is freed between two calls while other sessions wait. Null (the default) frees none by age.</summary>
    public TimeSpan? EditorLockHardCap { get; set; }

    /// <summary>The holder of an explicit claim past the 15-minute cap is told to release with every Unity tool result.</summary>
    public bool EditorLockCapNudge { get; set; } = true;

    /// <summary>
    /// Read-only calls of the holder renew its claim like any other call. Off (the default), they renew it only while the
    /// editor compiles, reloads, imports, builds, runs tests or is in the play session the holder started.
    /// </summary>
    public bool EditorLockReadOnlyKeepsAlive { get; set; }

    /// <summary>An agent's editor claim with no tool call for this long is freed.</summary>
    public TimeSpan EditorLockIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The idle limit while other sessions wait for the lock.</summary>
    public TimeSpan EditorLockWaitingIdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The idle limit of a holder in the play session it started itself, whether others wait or not.</summary>
    public TimeSpan EditorLockPlayIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long play mode an agent left on stays reserved for that agent before the queue moves on.</summary>
    public TimeSpan EditorLockAbandonedPlayGrace { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long one acquire call waits in the queue before it answers queued.</summary>
    public TimeSpan EditorLockWaitSlice { get; set; } = TimeSpan.FromSeconds(50);

    /// <summary>A queued session that is not waiting in a call is dropped this long after its slice ended.</summary>
    public TimeSpan EditorLockTicketGrace { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a queued agent waits for a name in its 'after' that nobody has used yet, before its acquire fails.</summary>
    public TimeSpan EditorLockAfterGrace { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>A session granted the lock from the queue loses it when it makes no tool call for this long.</summary>
    public TimeSpan EditorLockFirstCallDeadline { get; set; } = TimeSpan.FromSeconds(60);

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
