using Feeder.Matrix.Protocol;
using Feeder.Matrix.Unity;

namespace Feeder.Matrix.UnitTests;

public class UnityLinkRegistryTests
{
    private static UnityRegisterRequest MakeRequest(string projectId = "proj", string instanceId = "inst-1") => new()
    {
        ProtocolVersion = "1",
        ProjectId = projectId,
        UnityInstanceId = instanceId,
        ProjectPathHash = projectId,
        UnityVersion = "6000.0.32f1",
        PluginVersion = "0.1.0",
        SupportedProtocolVersions = ["1"],
        ToolRegistryHash = "hash",
        AuthToken = "token",
    };

    [Fact]
    public void Register_creates_link_with_session()
    {
        var registry = new UnityLinkRegistry();
        var link = registry.Register(MakeRequest(), "conn-1");

        Assert.Equal("conn-1", link.ConnectionId);
        Assert.True(link.Connected);
        Assert.NotEmpty(link.SessionId);
        Assert.Same(link, registry.GetDefaultLink());
    }

    [Fact]
    public void Reregister_same_instance_resumes_and_keeps_registry()
    {
        var registry = new UnityLinkRegistry();
        var first = registry.Register(MakeRequest(), "conn-1");
        first.Registry = new RegistrySnapshot { ProjectId = "proj", RegistryHash = "h1" };

        registry.MarkDisconnected("conn-1");
        Assert.Null(registry.GetDefaultLink());

        var resumed = registry.Register(MakeRequest(), "conn-2");
        Assert.Same(first, resumed);
        Assert.Equal("conn-2", resumed.ConnectionId);
        Assert.True(resumed.Connected);
        Assert.NotNull(resumed.Registry); // registry cache survives reconnect (domain reload)
    }

    [Fact]
    public void Reregister_different_instance_wins()
    {
        var registry = new UnityLinkRegistry();
        registry.Register(MakeRequest(instanceId: "inst-1"), "conn-1");
        var newer = registry.Register(MakeRequest(instanceId: "inst-2"), "conn-2");

        Assert.Same(newer, registry.GetLink("proj"));
        Assert.Equal("inst-2", newer.UnityInstanceId);
    }

    [Fact]
    public async Task Operation_completes_waiter()
    {
        var registry = new UnityLinkRegistry();
        var tcs = registry.CreateOperation("op-1");

        var completed = registry.CompleteOperation(new ToolResult { OperationId = "op-1", Success = true });
        Assert.True(completed);
        var result = await tcs.Completion.Task;
        Assert.True(result.Success);
    }

    [Fact]
    public void Completing_unknown_or_abandoned_operation_returns_false()
    {
        var registry = new UnityLinkRegistry();
        Assert.False(registry.CompleteOperation(new ToolResult { OperationId = "nope", Success = true }));

        registry.CreateOperation("op-1");
        registry.AbandonOperation("op-1");
        Assert.False(registry.CompleteOperation(new ToolResult { OperationId = "op-1", Success = true }));
    }

    [Fact]
    public void Duplicate_operation_id_throws()
    {
        var registry = new UnityLinkRegistry();
        registry.CreateOperation("op-1");
        Assert.Throws<InvalidOperationException>(() => registry.CreateOperation("op-1"));
    }

    [Fact]
    public void Unregister_removes_only_matching_instance()
    {
        var registry = new UnityLinkRegistry();
        registry.Register(MakeRequest(instanceId: "inst-1"), "conn-1");

        registry.Unregister("proj", "other-instance");
        Assert.NotNull(registry.GetLink("proj"));

        registry.Unregister("proj", "inst-1");
        Assert.Null(registry.GetLink("proj"));
    }
}
