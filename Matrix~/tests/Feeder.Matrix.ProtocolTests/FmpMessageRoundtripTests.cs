using Feeder.Matrix.Protocol;
using MessagePack;

namespace Feeder.Matrix.ProtocolTests;

public class FmpMessageRoundtripTests
{
    private static T Roundtrip<T>(T value) => MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value));

    [Fact]
    public void UnityRegisterRequest_roundtrips()
    {
        var request = new UnityRegisterRequest
        {
            ProtocolVersion = "1",
            ProjectId = "abc123",
            UnityInstanceId = Guid.NewGuid().ToString("N"),
            ProjectPathHash = "abc123",
            UnityVersion = "6000.0.32f1",
            PluginVersion = "0.82.4",
            SupportedProtocolVersions = ["1"],
            ToolRegistryHash = "deadbeef",
            AuthToken = "secret",
            EditorState = FmpEditorState.Compiling,
            PendingOperations = ["op1", "op2"],
        };

        var back = Roundtrip(request);
        Assert.Equal(request.ProjectId, back.ProjectId);
        Assert.Equal(request.UnityInstanceId, back.UnityInstanceId);
        Assert.Equal(request.UnityVersion, back.UnityVersion);
        Assert.Equal(request.SupportedProtocolVersions, back.SupportedProtocolVersions);
        Assert.Equal(request.ToolRegistryHash, back.ToolRegistryHash);
        Assert.Equal(request.AuthToken, back.AuthToken);
        Assert.Equal(FmpEditorState.Compiling, back.EditorState);
        Assert.Equal(request.PendingOperations, back.PendingOperations);
    }

    [Fact]
    public void ToolInvoke_preserves_utf8_payload_bytes()
    {
        var argsJson = """{"message":"xin chào 🎮"}"""u8.ToArray();
        var invoke = new ToolInvoke
        {
            OperationId = "op",
            ToolName = "ping",
            ArgumentsJsonUtf8 = argsJson,
            DeadlineUnixMs = 1234567890,
            TraceId = "trace",
        };

        var back = Roundtrip(invoke);
        Assert.Equal(argsJson, back.ArgumentsJsonUtf8);
        Assert.Equal(invoke.DeadlineUnixMs, back.DeadlineUnixMs);
    }

    [Fact]
    public void ToolResult_error_branch_roundtrips()
    {
        var result = new ToolResult
        {
            OperationId = "op",
            Success = false,
            Error = new FmpError
            {
                Code = FmpErrorCodes.ToolFailed,
                Message = "boom",
                Retriable = true,
                DetailsJsonUtf8 = """{"stack":"..."}"""u8.ToArray(),
            },
        };

        var back = Roundtrip(result);
        Assert.False(back.Success);
        Assert.NotNull(back.Error);
        Assert.Equal(FmpErrorCodes.ToolFailed, back.Error!.Code);
        Assert.True(back.Error.Retriable);
    }

    [Fact]
    public void RegistrySnapshot_roundtrips_tool_descriptors()
    {
        var snapshot = new RegistrySnapshot
        {
            ProjectId = "p",
            RegistryHash = "h",
            Tools =
            [
                new ToolDescriptor
                {
                    Name = "assets-find",
                    Description = "Search assets",
                    InputSchemaJsonUtf8 = """{"type":"object"}"""u8.ToArray(),
                    ReadOnly = true,
                    ThreadSafe = true,
                },
            ],
        };

        var back = Roundtrip(snapshot);
        Assert.Single(back.Tools);
        Assert.Equal("assets-find", back.Tools[0].Name);
        Assert.True(back.Tools[0].ReadOnly);
        Assert.True(back.Tools[0].ThreadSafe);
        Assert.False(back.Tools[0].Destructive);
    }

    [Fact]
    public void UnityRegisterResponse_defaults_survive_roundtrip()
    {
        var response = new UnityRegisterResponse
        {
            SessionId = "s",
            ChosenProtocolVersion = "1",
        };

        var back = Roundtrip(response);
        Assert.Equal(FmpConstants.DefaultQueueCapacity, back.QueueCapacity);
        Assert.Equal(FmpConstants.HeartbeatIntervalSeconds, back.HeartbeatIntervalSeconds);
        Assert.Null(back.Error);
    }
}
