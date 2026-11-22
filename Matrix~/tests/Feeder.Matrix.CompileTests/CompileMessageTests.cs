using System.Buffers;
using System.Text;
using Feeder.Matrix.Protocol;
using MessagePack;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Feeder.Matrix.CompileTests;

public class CompileMessageTests
{
    private static T Roundtrip<T>(T value) => MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value));

    [Fact]
    public void CompileRequest_roundtrips()
    {
        var request = new CompileRequest { Source = "class C { }", ReferencePaths = ["a.dll"], AssemblyName = "X", SyntaxOnly = true, TimeoutMs = 5 };

        var back = Roundtrip(request);

        Assert.Equal(request.Source, back.Source);
        Assert.Equal(request.ReferencePaths, back.ReferencePaths);
        Assert.Equal("X", back.AssemblyName);
        Assert.True(back.SyntaxOnly);
        Assert.Equal(5, back.TimeoutMs);
    }

    [Fact]
    public void CompileResponse_roundtrips()
    {
        var response = new CompileResponse { Success = false, Assembly = [1, 2], Diagnostics = ["(1,1): error CS1000: x"], Error = "e" };

        var back = Roundtrip(response);

        Assert.False(back.Success);
        Assert.Equal(response.Assembly, back.Assembly);
        Assert.Equal(response.Diagnostics, back.Diagnostics);
        Assert.Equal("e", back.Error);
    }

    [Fact]
    public void Register_response_capabilities_roundtrip()
    {
        var response = new UnityRegisterResponse { SessionId = "s", ChosenProtocolVersion = "1", Capabilities = [FmpCapabilities.Compile] };

        Assert.Equal(new[] { "compile" }, Roundtrip(response).Capabilities);
        Assert.Empty(new UnityRegisterResponse { SessionId = "s", ChosenProtocolVersion = "1" }.Capabilities);
    }

    [Fact]
    public void Json_wire_shape_is_camel_case_with_a_base64_image()
    {
        var protocol = new JsonHubProtocol();

        var request = Write(protocol, new InvocationMessage("1", "Compile", [new CompileRequest { Source = "class C { }", ReferencePaths = [@"C:\a.dll"] }]));
        var response = Write(protocol, CompletionMessage.WithResult("1", new CompileResponse { Success = true, Assembly = [1, 2, 3] }));

        Assert.Contains("""{"source":"class C { }","referencePaths":["C:\\a.dll"],"assemblyName":"DynamicAssembly","syntaxOnly":false,"timeoutMs":0}""", request);
        Assert.Contains("""{"success":true,"assembly":"AQID","diagnostics":[],"error":null}""", response);
    }

    private static string Write(IHubProtocol protocol, HubMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(message, buffer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
