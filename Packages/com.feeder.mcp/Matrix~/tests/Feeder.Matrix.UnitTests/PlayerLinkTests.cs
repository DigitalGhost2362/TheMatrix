using System.Buffers;
using System.Text;
using Feeder.Matrix.Backends;
using Feeder.Matrix.Health;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Unity;
using MessagePack;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.UnitTests;

public class PlayerLinkTests
{
    private static UnityRegisterRequest Request(string instance) => new()
    {
        ProtocolVersion = "1", ProjectId = "proj", UnityInstanceId = instance, ProjectPathHash = "h", UnityVersion = "6",
        PluginVersion = "1", SupportedProtocolVersions = ["1"], ToolRegistryHash = "x", AuthToken = "t",
    };

    private static ToolDescriptor Tool(string name, string? target = null)
        => new() { Name = name, InputSchemaJsonUtf8 = Encoding.UTF8.GetBytes("{\"type\":\"object\"}"), Target = target };

    private static (UnityLinkRegistry Registry, UnityLink Editor, UnityLink Player) Linked()
    {
        var registry = new UnityLinkRegistry();
        var editor = registry.Register(Request("editor-1"), "conn-editor");
        editor.Registry = new RegistrySnapshot { ProjectId = "proj", RegistryHash = "e", Tools = [Tool("scene-open")] };
        var player = registry.RegisterPlayer(Request("player-1"), "conn-player");
        player.Registry = new RegistrySnapshot { ProjectId = "proj", RegistryHash = "p", Tools = [Tool("runtime-ping", FmpToolTargets.Runtime)] };
        return (registry, editor, player);
    }

    private static UnityLinkBackend Backend(UnityLinkRegistry registry, FakeHub hub)
    {
        var options = Options.Create(new MatrixOptions());
        return new UnityLinkBackend(registry, hub, new EditorHealth(registry, options, TimeProvider.System), options, NullLogger<UnityLinkBackend>.Instance);
    }

    [Fact]
    public void Player_link_does_not_replace_the_editor_link()
    {
        var (registry, editor, player) = Linked();

        Assert.Same(editor, registry.GetLink("proj"));
        Assert.Same(editor, registry.GetDefaultLink());
        Assert.Single(registry.Links);
        Assert.False(editor.IsPlayer);
        Assert.True(player.IsPlayer);
        Assert.Same(player, registry.GetPlayerLink("proj"));
        Assert.Equal("conn-editor", editor.ConnectionId);
    }

    [Fact]
    public async Task List_shows_editor_tools_and_runtime_tools_of_the_player()
    {
        var (registry, _, _) = Linked();
        var tools = await Backend(registry, new FakeHub(registry)).ListToolsAsync(CancellationToken.None);

        Assert.Equal(["scene-open", "runtime-ping"], tools.Select(t => t.Name));
    }

    [Fact]
    public async Task Runtime_tool_routes_to_the_player_and_editor_tool_to_the_editor()
    {
        var (registry, _, _) = Linked();
        var hub = new FakeHub(registry);
        var backend = Backend(registry, hub);

        var runtime = await backend.CallToolAsync(new CallToolRequestParams { Name = "runtime-ping" }, CancellationToken.None);
        var editor = await backend.CallToolAsync(new CallToolRequestParams { Name = "scene-open" }, CancellationToken.None);

        Assert.Equal([("conn-player", "runtime-ping"), ("conn-editor", "scene-open")], hub.Invoked);
        Assert.Equal("ok:runtime-ping", ((TextContentBlock)runtime.Content![0]).Text);
        Assert.Equal("ok:scene-open", ((TextContentBlock)editor.Content![0]).Text);
    }

    [Fact]
    public async Task Runtime_tool_does_not_run_on_an_editor_that_has_a_tool_of_that_name()
    {
        var (registry, editor, _) = Linked();
        editor.Registry = new RegistrySnapshot { ProjectId = "proj", RegistryHash = "e", Tools = [Tool("runtime-ping")] };
        var hub = new FakeHub(registry);

        await Backend(registry, hub).CallToolAsync(new CallToolRequestParams { Name = "runtime-ping" }, CancellationToken.None);

        Assert.Equal([("conn-editor", "runtime-ping")], hub.Invoked);
    }

    [Fact]
    public void A_player_drop_removes_the_player_and_leaves_the_editor()
    {
        var (registry, editor, _) = Linked();

        registry.MarkDisconnected("conn-player");

        Assert.Null(registry.GetPlayerLink("proj"));
        Assert.False(registry.IsRuntimeTool("runtime-ping"));
        Assert.True(editor.Connected);
        Assert.Same(editor, registry.GetDefaultLink());
    }

    [Fact]
    public void An_editor_drop_leaves_the_player_linked()
    {
        var (registry, editor, player) = Linked();

        registry.MarkDisconnected("conn-editor");

        Assert.False(editor.Connected);
        Assert.True(player.Connected);
        Assert.True(registry.IsRuntimeTool("RUNTIME-PING"));
    }

    [Fact]
    public async Task Player_lost_mid_call_ends_the_call_with_an_error()
    {
        var (registry, _, _) = Linked();
        var hub = new FakeHub(registry) { Silent = true };
        var call = Backend(registry, hub).CallToolAsync(new CallToolRequestParams { Name = "runtime-ping" }, CancellationToken.None).AsTask();

        await Task.Delay(100);
        registry.MarkDisconnected("conn-player");
        var result = await call.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.IsError);
        Assert.Contains("player", ((TextContentBlock)result.Content![0]).Text);
    }

    [Fact]
    public void A_descriptor_from_an_older_plugin_without_a_target_reads_as_an_editor_tool()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(8);
        writer.Write("old-tool");
        writer.WriteNil();
        writer.WriteNil();
        writer.Write(Encoding.UTF8.GetBytes("{}"));
        writer.Write(false);
        writer.Write(false);
        writer.Write(false);
        writer.Write(false);
        writer.Flush();

        var descriptor = MessagePackSerializer.Deserialize<ToolDescriptor>(buffer.WrittenMemory);

        Assert.Equal("old-tool", descriptor.Name);
        Assert.Null(descriptor.Target);
    }

    [Fact]
    public void A_descriptor_with_a_target_survives_a_round_trip()
    {
        var bytes = MessagePackSerializer.Serialize(Tool("runtime-ping", FmpToolTargets.Runtime));
        Assert.Equal(FmpToolTargets.Runtime, MessagePackSerializer.Deserialize<ToolDescriptor>(bytes).Target);
    }

    private sealed class FakeHub(UnityLinkRegistry registry) : IHubContext<FmpHub, IFmpUnityClient>
    {
        public List<(string Connection, string Tool)> Invoked { get; } = [];
        public bool Silent { get; set; }
        public IHubClients<IFmpUnityClient> Clients => new FakeClients(this, registry);
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class FakeClients(FakeHub hub, UnityLinkRegistry registry) : IHubClients<IFmpUnityClient>
    {
        public IFmpUnityClient All => throw new NotSupportedException();
        public IFmpUnityClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IFmpUnityClient Client(string connectionId) => new FakeClient(hub, registry, connectionId);
        public IFmpUnityClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IFmpUnityClient Group(string groupName) => throw new NotSupportedException();
        public IFmpUnityClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IFmpUnityClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IFmpUnityClient User(string userId) => throw new NotSupportedException();
        public IFmpUnityClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeClient(FakeHub hub, UnityLinkRegistry registry, string connection) : IFmpUnityClient
    {
        public Task InvokeTool(ToolInvoke invoke)
        {
            hub.Invoked.Add((connection, invoke.ToolName));
            if (hub.Silent)
                return Task.CompletedTask;
            var content = "{\"content\":[{\"type\":\"text\",\"text\":\"ok:" + invoke.ToolName + "\"}]}";
            registry.CompleteOperation(new ToolResult { OperationId = invoke.OperationId, Success = true, ContentJsonUtf8 = Encoding.UTF8.GetBytes(content) });
            return Task.CompletedTask;
        }

        public Task CancelTool(string operationId) => Task.CompletedTask;
        public Task McpClientsChanged(McpClientInfo[] clients) => Task.CompletedTask;
        public Task EditorLockChanged(EditorLockState state) => Task.CompletedTask;
    }
}
