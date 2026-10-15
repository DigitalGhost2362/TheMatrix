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

        var progress = JsonSerializer.Deserialize<ToolProgress>("""{"operationId":"op","progress":0.5}""", HubJson)!;
        Assert.False(progress.Deferred);
        Assert.Equal(0, progress.ExtendDeadlineMs);
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
