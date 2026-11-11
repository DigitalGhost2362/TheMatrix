using System.Text.Json;

namespace Feeder.Matrix.Tools;

/// <summary>Who makes a call: an MCP session and, inside it, the main agent (empty id) or one of its subagents.</summary>
public readonly record struct LockCaller(string? SessionId, string AgentId = "", string AgentType = "")
{
    public static implicit operator LockCaller(string? sessionId) => new(sessionId);

    public bool IsSubagent => AgentId.Length > 0;

    /// <summary>One value per claimant; null without a session.</summary>
    public string? Key => string.IsNullOrEmpty(SessionId) ? null : IsSubagent ? $"{SessionId}#{AgentId}" : SessionId;
}

/// <summary>
/// Tells the subagents of one MCP session apart. Subagents call tools over their parent's MCP connection, so the
/// session id alone names all of them. Claude Code posts its hook events to <see cref="HookPath"/>; the tool call that
/// follows over MCP carries the same tool use id in <c>_meta</c>, which names the agent that made it. A session without
/// the hook stays one caller.
/// </summary>
public sealed class AgentDirectory(TimeProvider time, ILogger<AgentDirectory> logger)
{
    public const string HookPath = "/hooks/claude";
    public const string ToolUseIdMeta = "claudecode/toolUseId";

    static readonly TimeSpan Kept = TimeSpan.FromMinutes(30);
    // a main agent that calls another tool this long after spawning is no longer waiting in that spawn
    static readonly TimeSpan SpawnSettle = TimeSpan.FromSeconds(5);
    static readonly string[] SpawnTools = ["Agent", "Task"];

    public enum Known { Unknown, Live, Ended }

    sealed record Use(string ClientSession, string AgentId, string AgentType, DateTimeOffset At);

    sealed class Agent
    {
        public required string ClientSession { get; init; }
        public string Type { get; set; } = string.Empty;
        public string? McpSession { get; set; }
        public DateTimeOffset? Ended { get; set; }
    }

    readonly object _gate = new();
    readonly Dictionary<string, Use> _uses = [];
    readonly Dictionary<string, Agent> _agents = [];
    // the client's own session id (hooks) -> the MCP session it calls tools on
    readonly Dictionary<string, string> _mcpSessions = [];
    // per client session: spawn calls of the main agent that have not returned
    readonly Dictionary<string, Dictionary<string, DateTimeOffset>> _spawning = [];

    /// <summary>Raised with the MCP session and the agent id when a subagent ends.</summary>
    public event Action<string, string>? AgentEnded;

    /// <summary>Raised with the old and the new MCP session when a client that reported agents on one calls on another (it reconnected).</summary>
    public event Action<string, string>? SessionReplaced;

    /// <summary>Takes one hook event as Claude Code posts it. Unknown events and shapes are ignored.</summary>
    public void OnHook(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return;
        var session = Read(body, "session_id");
        var agentId = Read(body, "agent_id");
        var agentType = Read(body, "agent_type");
        var toolUseId = Read(body, "tool_use_id");
        var toolName = Read(body, "tool_name");
        string? endedOn = null;
        lock (_gate)
        {
            var now = time.GetUtcNow();
            Prune(now);
            switch (Read(body, "hook_event_name"))
            {
                case "PreToolUse" when toolUseId.Length > 0:
                    _uses[toolUseId] = new Use(session, agentId, agentType, now);
                    if (agentId.Length > 0)
                        Live(session, agentId, agentType);
                    else if (SpawnTools.Contains(toolName))
                        Spawning(session)[toolUseId] = now;
                    else if (_spawning.TryGetValue(session, out var open))
                        foreach (var old in open.Where(s => now - s.Value > SpawnSettle).Select(s => s.Key).ToArray())
                            open.Remove(old);
                    break;
                case "PostToolUse" or "PostToolUseFailure" when _spawning.TryGetValue(session, out var spawns):
                    spawns.Remove(toolUseId);
                    break;
                case "SubagentStart" when agentId.Length > 0:
                    Live(session, agentId, agentType);
                    break;
                case "SubagentStop" when agentId.Length > 0:
                    var agent = Live(session, agentId, agentType);
                    agent.Ended = now;
                    endedOn = agent.McpSession ?? _mcpSessions.GetValueOrDefault(session);
                    break;
                case "SessionEnd":
                    _spawning.Remove(session);
                    break;
            }
        }
        if (endedOn is not null)
        {
            logger.LogInformation("Subagent {AgentType} {AgentId} of session {SessionId} ended", agentType, agentId, endedOn);
            AgentEnded?.Invoke(endedOn, agentId);
        }
    }

    /// <summary>The caller of a tool call that arrived on this MCP session with this tool use id.</summary>
    public LockCaller Resolve(string? mcpSession, string? toolUseId)
    {
        if (string.IsNullOrEmpty(mcpSession) || string.IsNullOrEmpty(toolUseId))
            return new LockCaller(mcpSession);
        string? replaced;
        LockCaller caller;
        lock (_gate)
        {
            if (!_uses.Remove(toolUseId, out var use))
                return new LockCaller(mcpSession);
            replaced = _mcpSessions.GetValueOrDefault(use.ClientSession);
            _mcpSessions[use.ClientSession] = mcpSession;
            caller = new LockCaller(mcpSession, use.AgentId, use.AgentType);
            if (use.AgentId.Length > 0)
                Live(use.ClientSession, use.AgentId, use.AgentType).McpSession = mcpSession;
            if (replaced is not null && replaced != mcpSession)
                foreach (var agent in _agents.Values.Where(a => a.McpSession == replaced))
                    agent.McpSession = mcpSession;
        }
        if (replaced is not null && replaced != mcpSession)
        {
            logger.LogInformation("MCP session {Old} of a client continues as {Current}", replaced, mcpSession);
            SessionReplaced?.Invoke(replaced, mcpSession);
        }
        return caller;
    }

    /// <summary>True while the main agent of this MCP session waits for a subagent it spawned, so it cannot use the editor itself.</summary>
    public bool MainAgentWaits(string mcpSession)
    {
        lock (_gate)
            return _mcpSessions.Any(pair => pair.Value == mcpSession && _spawning.TryGetValue(pair.Key, out var open) && open.Count > 0);
    }

    /// <summary>True when this MCP session reports its agents through the hook.</summary>
    public bool Reports(string? mcpSession)
    {
        lock (_gate)
            return mcpSession is not null && _mcpSessions.ContainsValue(mcpSession);
    }

    /// <summary>Whether subagents of this type are running, all ended, or never seen.</summary>
    public Known TypeState(string agentType)
    {
        lock (_gate)
        {
            var known = Known.Unknown;
            foreach (var agent in _agents.Values)
            {
                if (!string.Equals(agent.Type, agentType, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (agent.Ended is null)
                    return Known.Live;
                known = Known.Ended;
            }
            return known;
        }
    }

    /// <summary>The types of the subagents running now.</summary>
    public string[] LiveTypes()
    {
        lock (_gate)
            return _agents.Values.Where(a => a.Ended is null && a.Type.Length > 0).Select(a => a.Type).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // caller holds _gate
    Agent Live(string session, string agentId, string agentType)
    {
        if (!_agents.TryGetValue(agentId, out var agent))
            _agents[agentId] = agent = new Agent { ClientSession = session };
        if (agentType.Length > 0)
            agent.Type = agentType;
        return agent;
    }

    // caller holds _gate
    Dictionary<string, DateTimeOffset> Spawning(string session)
    {
        if (!_spawning.TryGetValue(session, out var open))
            _spawning[session] = open = [];
        return open;
    }

    // caller holds _gate
    void Prune(DateTimeOffset now)
    {
        foreach (var id in _uses.Where(u => now - u.Value.At > Kept).Select(u => u.Key).ToArray())
            _uses.Remove(id);
        foreach (var id in _agents.Where(a => a.Value.Ended is { } ended && now - ended > Kept).Select(a => a.Key).ToArray())
            _agents.Remove(id);
        foreach (var open in _spawning.Values)
            foreach (var id in open.Where(s => now - s.Value > Kept).Select(s => s.Key).ToArray())
                open.Remove(id);
    }

    static string Read(JsonElement json, string name)
        => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
