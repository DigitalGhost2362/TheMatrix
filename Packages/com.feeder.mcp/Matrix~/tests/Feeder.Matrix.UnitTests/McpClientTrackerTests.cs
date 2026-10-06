using Feeder.Matrix.Protocol;
using Feeder.Matrix.Unity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.UnitTests;

public class McpClientTrackerTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingHub _hub = new();
    private readonly McpClientTracker _tracker;

    public McpClientTrackerTests()
        => _tracker = new McpClientTracker(_hub, _time, NullLogger<McpClientTracker>.Instance);

    private void Register(string sessionId)
        => _tracker.Register(sessionId, () => new Implementation { Name = "claude-code", Version = "1.0" });

    [Fact]
    public void Session_is_keyed_by_transport_session_id()
    {
        Register("sid-1");

        var client = Assert.Single(_tracker.Snapshot());
        Assert.Equal("sid-1", client.SessionId);
        Assert.Equal("claude-code", client.ClientName);
        Assert.True(client.IsConnected);
        Assert.False(_tracker.Touch("unknown-sid"));
    }

    [Fact]
    public void Session_past_active_window_is_idle_but_kept()
    {
        Register("sid-1");

        _time.Advance(McpClientTracker.ActiveWindow);
        Assert.True(Assert.Single(_tracker.Snapshot()).IsConnected);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(Assert.Single(_tracker.Snapshot()).IsConnected);
    }

    [Fact]
    public void Touch_revives_idle_session_and_reports_flip()
    {
        Register("sid-1");

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(_tracker.Touch("sid-1"));

        _time.Advance(McpClientTracker.ActiveWindow + TimeSpan.FromSeconds(1));
        Assert.True(_tracker.Touch("sid-1"));
        Assert.True(Assert.Single(_tracker.Snapshot()).IsConnected);
        Assert.False(_tracker.Touch("sid-1"));
    }

    [Fact]
    public async Task Broadcasts_only_when_active_count_changes()
    {
        Register("sid-1");
        await _tracker.BroadcastAsync();
        _hub.Calls.Clear();

        Assert.False(await _tracker.BroadcastIfActiveCountChangedAsync());

        _time.Advance(TimeSpan.FromMinutes(5));
        Register("sid-2");
        await _tracker.BroadcastAsync();
        _hub.Calls.Clear();

        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.True(await _tracker.BroadcastIfActiveCountChangedAsync());
        var sent = Assert.Single(_hub.Calls);
        Assert.Equal(["sid-2"], sent.Where(c => c.IsConnected).Select(c => c.SessionId));

        Assert.False(await _tracker.BroadcastIfActiveCountChangedAsync());
        Assert.Single(_hub.Calls);

        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await _tracker.BroadcastIfActiveCountChangedAsync());
        Assert.DoesNotContain(_hub.Calls[^1], c => c.IsConnected);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class RecordingHub : IHubContext<FmpHub, IFmpUnityClient>
    {
        private readonly RecordingClients _clients = new();

        public List<McpClientInfo[]> Calls => _clients.Calls;
        public IHubClients<IFmpUnityClient> Clients => _clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingClients : IHubClients<IFmpUnityClient>, IFmpUnityClient
    {
        public List<McpClientInfo[]> Calls { get; } = [];

        public IFmpUnityClient All => this;
        public IFmpUnityClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IFmpUnityClient Client(string connectionId) => throw new NotSupportedException();
        public IFmpUnityClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IFmpUnityClient Group(string groupName) => throw new NotSupportedException();
        public IFmpUnityClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IFmpUnityClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IFmpUnityClient User(string userId) => throw new NotSupportedException();
        public IFmpUnityClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

        public Task InvokeTool(ToolInvoke invoke) => throw new NotSupportedException();
        public Task CancelTool(string operationId) => throw new NotSupportedException();

        public Task McpClientsChanged(McpClientInfo[] clients)
        {
            Calls.Add(clients);
            return Task.CompletedTask;
        }
    }
}
