using System.Text.Json;
using Feeder.Matrix.Protocol;
using MessagePack;

namespace Feeder.Matrix.ProtocolTests;

// New keys are appended only: an old plugin talking to a new matrix and a new plugin talking to an
// old matrix must both still parse every message (protocol spec, "only append keys").
public class FmpCompatibilityTests
{
    // the hub's JSON protocol: camelCase names, case-insensitive reads (what the Unity plugin speaks)
    private static readonly JsonSerializerOptions HubJson = new(JsonSerializerDefaults.Web);

    [MessagePackObject]
    public sealed record OldHeartbeat
    {
        [Key(0)] public required string UnityInstanceId { get; init; }
        [Key(1)] public FmpEditorState EditorState { get; init; }
    }

    [MessagePackObject]
    public sealed record OldProgress
    {
        [Key(0)] public required string OperationId { get; init; }
        [Key(1)] public double? Progress { get; init; }
        [Key(2)] public string? Message { get; init; }
    }

    [Fact]
    public void Old_plugin_heartbeat_json_parses_with_defaults()
    {
        var beat = JsonSerializer.Deserialize<HeartbeatMessage>("""{"unityInstanceId":"u","editorState":3}""", HubJson)!;
        Assert.Equal("u", beat.UnityInstanceId);
        Assert.Equal(FmpEditorState.Playing, beat.EditorState);
        Assert.Equal(0, beat.LastMainTickUnixMs);
        Assert.Empty(beat.InFlight);
        Assert.Null(beat.ConsoleLogPath);
    }

    [Fact]
    public void New_plugin_heartbeat_json_parses_every_field()
    {
        var beat = JsonSerializer.Deserialize<HeartbeatMessage>(
            """{"unityInstanceId":"u","editorState":0,"lastMainTickUnixMs":5,"updating":true,"focused":true,"processId":7,"consoleLogPath":"C:/log","inFlight":[{"operationId":"op","toolName":"script-execute","startedUnixMs":4}]}""",
            HubJson)!;
        Assert.Equal(5, beat.LastMainTickUnixMs);
        Assert.True(beat.Updating);
        Assert.Equal(7, beat.ProcessId);
        Assert.Equal("script-execute", Assert.Single(beat.InFlight).ToolName);
    }

    [Fact]
    public void Old_plugin_register_and_progress_json_parse()
    {
        var register = JsonSerializer.Deserialize<UnityRegisterRequest>(
            """{"protocolVersion":"1","projectId":"p","unityInstanceId":"u","projectPathHash":"h","unityVersion":"6","pluginVersion":"0.87.1","supportedProtocolVersions":["1"],"toolRegistryHash":"x","authToken":"","editorState":0,"pendingOperations":[]}""",
            HubJson)!;
        Assert.Null(register.ProjectRoot);
        Assert.Null(register.McpEntryName);
        Assert.Empty(register.Features);

        var progress = JsonSerializer.Deserialize<ToolProgress>("""{"operationId":"op","progress":0.5}""", HubJson)!;
        Assert.False(progress.Deferred);
        Assert.Equal(0, progress.ExtendDeadlineMs);
    }

    [Fact]
    public void Editor_lock_state_reaches_the_plugin_in_its_json_shape()
    {
        var json = JsonSerializer.Serialize(new EditorLockState
        {
            Held = true,
            Kind = "agent",
            Owner = "claude-catwalk",
            Task = "catwalk score",
            ClientName = "claude-code",
            SessionId = "sid",
            AcquiredUnixMs = 5,
            ExpiresUnixMs = 9,
            CallsInFlight = 1,
        }, HubJson);

        Assert.Contains("\"owner\":\"claude-catwalk\"", json);
        Assert.Contains("\"callsInFlight\":1", json);
        var free = JsonSerializer.Deserialize<EditorLockState>("{}", HubJson)!;
        Assert.False(free.Held);
        Assert.Equal(string.Empty, free.Owner);
    }

    // the lock state of an editor that predates the queue
    public sealed record OldLockState
    {
        public bool Held { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Task { get; set; } = string.Empty;
        public int CallsInFlight { get; set; }
    }

    [MessagePackObject]
    public sealed record OldLockStateMessagePack
    {
        [Key(0)] public bool Held { get; init; }
        [Key(1)] public string Kind { get; set; } = string.Empty;
        [Key(2)] public string Owner { get; set; } = string.Empty;
        [Key(3)] public string Task { get; set; } = string.Empty;
        [Key(4)] public string ClientName { get; set; } = string.Empty;
        [Key(5)] public string ClientVersion { get; set; } = string.Empty;
        [Key(6)] public string SessionId { get; set; } = string.Empty;
        [Key(7)] public long AcquiredUnixMs { get; init; }
        [Key(8)] public long LastActivityUnixMs { get; init; }
        [Key(9)] public long ExpiresUnixMs { get; init; }
        [Key(10)] public int CallsInFlight { get; init; }
    }

    [Fact]
    public void Old_editor_still_gets_the_normal_lock_state_next_to_the_waiting_list()
    {
        var json = JsonSerializer.Serialize(new EditorLockState
        {
            Held = true,
            Kind = "agent",
            Owner = "claude-catwalk",
            Task = "catwalk score",
            CallsInFlight = 2,
            Waiting = [new EditorLockWaiter { Ticket = "t", Owner = "codex", Task = "shop", WaitingSinceUnixMs = 7, Attached = true }],
            QueuePaused = true,
        }, HubJson);

        Assert.Contains("\"waiting\":[{\"ticket\":\"t\"", json);
        var old = JsonSerializer.Deserialize<OldLockState>(json, HubJson)!;
        Assert.True(old.Held);
        Assert.Equal("claude-catwalk", old.Owner);
        Assert.Equal(2, old.CallsInFlight);

        var oldPacked = MessagePackSerializer.Deserialize<OldLockStateMessagePack>(MessagePackSerializer.Serialize(
            new EditorLockState { Held = true, Owner = "o", CallsInFlight = 2, Waiting = [new EditorLockWaiter { Ticket = "t" }] }));
        Assert.Equal("o", oldPacked.Owner);
        Assert.Equal(2, oldPacked.CallsInFlight);
    }

    [Fact]
    public void New_editor_reads_the_lock_state_of_a_server_without_a_queue()
    {
        var state = JsonSerializer.Deserialize<EditorLockState>("""{"held":true,"kind":"agent","owner":"claude","callsInFlight":1}""", HubJson)!;
        Assert.True(state.Held);
        Assert.Empty(state.Waiting);
        Assert.False(state.QueuePaused);
        Assert.False(state.QueueHeldByPlayMode);

        var packed = MessagePackSerializer.Deserialize<EditorLockState>(MessagePackSerializer.Serialize(
            new OldLockStateMessagePack { Held = true, Owner = "claude", CallsInFlight = 1 }));
        Assert.Equal("claude", packed.Owner);
        Assert.Empty(packed.Waiting);
    }

    [Fact]
    public void New_messages_parse_in_the_old_messagepack_shape()
    {
        var beat = new HeartbeatMessage
        {
            UnityInstanceId = "u",
            EditorState = FmpEditorState.Paused,
            LastMainTickUnixMs = 9,
            InFlight = [new InFlightOperation { OperationId = "op", ToolName = "t", StartedUnixMs = 1 }],
            ProcessId = 3,
        };
        var old = MessagePackSerializer.Deserialize<OldHeartbeat>(MessagePackSerializer.Serialize(beat));
        Assert.Equal(FmpEditorState.Paused, old.EditorState);

        var progress = new ToolProgress { OperationId = "op", Message = "m", Deferred = true, ExtendDeadlineMs = 10 };
        Assert.Equal("m", MessagePackSerializer.Deserialize<OldProgress>(MessagePackSerializer.Serialize(progress)).Message);
    }

    [Fact]
    public void Old_messagepack_shape_parses_as_new_message()
    {
        var beat = MessagePackSerializer.Deserialize<HeartbeatMessage>(
            MessagePackSerializer.Serialize(new OldHeartbeat { UnityInstanceId = "u", EditorState = FmpEditorState.Compiling }));
        Assert.Equal(FmpEditorState.Compiling, beat.EditorState);
        Assert.Equal(0, beat.ProcessId);
        Assert.Empty(beat.InFlight);
    }
}
