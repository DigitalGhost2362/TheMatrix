using System.Collections.Concurrent;
using Feeder.Matrix.Protocol;
using Microsoft.AspNetCore.SignalR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Feeder.Matrix.Unity;

/// <summary>
/// Tracks MCP client sessions on the matrix and mirrors them to linked Unity editors via the
/// FMP <c>McpClientsChanged</c> callback. A session counts as connected only while it has been
/// active within <see cref="ActiveWindow"/>; idle sessions stay registered and revive on the next request.
/// </summary>
public sealed class McpClientTracker(
    IHubContext<FmpHub, IFmpUnityClient> hub,
    TimeProvider time,
    ILogger<McpClientTracker> logger)
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _sessions = new();
    private int _lastBroadcastActiveCount;

    /// <summary>Runs one MCP session while keeping the tracker (and Unity) informed of its lifetime.</summary>
    public async Task RunSessionAsync(McpServer server, CancellationToken cancellationToken)
    {
        // Matches the Mcp-Session-Id header on streamable HTTP; null only for transports without one.
        var sessionId = server.SessionId ?? Guid.NewGuid().ToString("N");
        var entry = Register(sessionId, () => server.ClientInfo);
        try
        {
            var run = server.RunAsync(cancellationToken);
            _ = AnnounceWhenInitializedAsync(sessionId, server, cancellationToken);
            await run;
        }
        finally
        {
            _sessions.TryRemove(new KeyValuePair<string, Entry>(sessionId, entry));
            await BroadcastAsync();
        }
    }

    internal Entry Register(string sessionId, Func<Implementation?> clientInfo)
        => _sessions[sessionId] = new Entry(clientInfo, time.GetUtcNow().UtcTicks);

    /// <summary>Marks a session active; returns true when it was idle (or never seen active) before.</summary>
    public bool Touch(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry))
            return false;

        var now = time.GetUtcNow().UtcTicks;
        var previous = Interlocked.Exchange(ref entry.LastActivityTicks, now);
        return now - previous > ActiveWindow.Ticks;
    }

    public IReadOnlyList<(McpClientInfo Info, DateTimeOffset LastActivity)> Describe()
    {
        var now = time.GetUtcNow();
        return _sessions.Select(pair =>
        {
            var info = pair.Value.ClientInfo();
            var lastActivity = new DateTimeOffset(Volatile.Read(ref pair.Value.LastActivityTicks), TimeSpan.Zero);
            return (new McpClientInfo
            {
                SessionId = pair.Key,
                ClientName = info?.Name ?? string.Empty,
                ClientTitle = info?.Title ?? string.Empty,
                ClientVersion = info?.Version ?? string.Empty,
                IsConnected = now - lastActivity <= ActiveWindow,
            }, lastActivity);
        }).ToArray();
    }

    public McpClientInfo[] Snapshot() => Describe().Select(d => d.Info).ToArray();

    public async Task BroadcastAsync()
    {
        var snapshot = Snapshot();
        Volatile.Write(ref _lastBroadcastActiveCount, snapshot.Count(c => c.IsConnected));
        try
        {
            await hub.Clients.All.McpClientsChanged(snapshot);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Failed to broadcast MCP client change to Unity links");
        }
    }

    /// <summary>Broadcasts only when sessions crossed the idle threshold since the last broadcast.</summary>
    public async Task<bool> BroadcastIfActiveCountChangedAsync()
    {
        if (Snapshot().Count(c => c.IsConnected) == Volatile.Read(ref _lastBroadcastActiveCount))
            return false;

        await BroadcastAsync();
        return true;
    }

    // ClientInfo materializes once the MCP initialize request lands; wait briefly for it so the
    // notice carries the real client name instead of an empty placeholder.
    private async Task AnnounceWhenInitializedAsync(string sessionId, McpServer server, CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 0; i < 40 && server.ClientInfo is null && !cancellationToken.IsCancellationRequested; i++)
                await Task.Delay(50, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        if (_sessions.ContainsKey(sessionId))
            await BroadcastAsync();
    }

    internal sealed class Entry(Func<Implementation?> clientInfo, long lastActivityTicks)
    {
        public readonly Func<Implementation?> ClientInfo = clientInfo;
        public long LastActivityTicks = lastActivityTicks;
    }
}
