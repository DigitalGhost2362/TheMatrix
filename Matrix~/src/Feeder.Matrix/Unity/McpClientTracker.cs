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
    // tool names each session received in its last tools/list; tool-call only serves names outside it
    private readonly ConcurrentDictionary<string, HashSet<string>> _listedTools = new();
    private int _lastBroadcastActiveCount;

    /// <summary>Raised with the session id when an MCP session ends (the client closed it or it timed out).</summary>
    public event Action<string>? SessionEnded;

    /// <summary>Runs one MCP session while keeping the tracker (and Unity) informed of its lifetime.</summary>
    public async Task RunSessionAsync(McpServer server, CancellationToken cancellationToken)
    {
        // Matches the Mcp-Session-Id header on streamable HTTP; null only for transports without one.
        var sessionId = server.SessionId ?? Guid.NewGuid().ToString("N");
        var entry = Register(sessionId, () => server.ClientInfo, server);
        try
        {
            var run = server.RunAsync(cancellationToken);
            _ = AnnounceWhenInitializedAsync(sessionId, server, cancellationToken);
            await run;
        }
        finally
        {
            await EndAsync(sessionId, entry);
        }
    }

    internal async Task EndAsync(string sessionId, Entry entry)
    {
        _sessions.TryRemove(new KeyValuePair<string, Entry>(sessionId, entry));
        _listedTools.TryRemove(sessionId, out _);
        try
        {
            SessionEnded?.Invoke(sessionId);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "A SessionEnded handler failed for MCP session {SessionId}", sessionId);
        }
        await BroadcastAsync();
    }

    internal Entry Register(string sessionId, Func<Implementation?> clientInfo, McpServer? server = null)
        => _sessions[sessionId] = new Entry(clientInfo, time.GetUtcNow().UtcTicks, server);

    /// <summary>Remembers the tool names a session was just given by tools/list.</summary>
    public void RecordListed(string? sessionId, IEnumerable<string> toolNames)
    {
        if (!string.IsNullOrEmpty(sessionId))
            _listedTools[sessionId] = new HashSet<string>(toolNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when the session's last tools/list already held this tool, in any letter case.</summary>
    public bool WasListed(string? sessionId, string toolName)
        => !string.IsNullOrEmpty(sessionId) &&
           _listedTools.TryGetValue(sessionId, out var names) &&
           names.Contains(toolName);

    /// <summary>Tells every live MCP session to fetch tools/list again.</summary>
    public async Task NotifyToolListChangedAsync()
    {
        foreach (var (sessionId, entry) in _sessions.ToArray())
        {
            if (entry.Server is null)
                continue;
            try
            {
                await entry.Server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Failed to send tools/list_changed to MCP session {SessionId}", sessionId);
            }
        }
    }

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

    /// <summary>Name and version the client sent in initialize, or null for an unknown session.</summary>
    public Implementation? ClientOf(string? sessionId)
        => sessionId is not null && _sessions.TryGetValue(sessionId, out var entry) ? entry.ClientInfo() : null;

    /// <summary>True while the session is open on the matrix.</summary>
    public bool IsOpen(string? sessionId) => sessionId is not null && _sessions.ContainsKey(sessionId);

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

    internal sealed class Entry(Func<Implementation?> clientInfo, long lastActivityTicks, McpServer? server = null)
    {
        public readonly Func<Implementation?> ClientInfo = clientInfo;
        public readonly McpServer? Server = server;
        public long LastActivityTicks = lastActivityTicks;
    }
}
