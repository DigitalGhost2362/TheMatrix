using System.Text.Encodings.Web;
using System.Text.Json;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Unity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Feeder.Matrix.Tools;

/// <summary>
/// One claim on the Unity editor at a time, held by an MCP session (an agent).
/// While someone holds it, every other session gets its editor-changing tools refused; read-only tools always run.
/// The claim ends on release, when the holder's MCP session closes, or when an agent stays idle past
/// <see cref="MatrixOptions.EditorLockIdleTimeout"/>. It is mirrored to <c>Temp/agent-editor-lock/lock.json</c> so
/// sessions that still claim with the lock-file scripts see it, and a live lock file written by such a session
/// keeps the claim from being taken.
/// </summary>
public sealed class EditorLock
{
    public const string AcquireName = "editor-lock-acquire";
    public const string ReleaseName = "editor-lock-release";
    public const string StatusName = "editor-lock-status";

    public const string KindAgent = "agent";
    public const string KindOutside = "outside";

    /// <summary>Longest a claim should last; past it the editor and other sessions are told, but nothing is freed.</summary>
    public static readonly TimeSpan Cap = TimeSpan.FromMinutes(15);

    // what the lock-file scripts treat as stale
    static readonly TimeSpan FileTtl = TimeSpan.FromMinutes(20);
    static readonly TimeSpan MirrorBeat = TimeSpan.FromMinutes(1);
    const int MaxTextLength = 120;

    // not marked read-only, yet they change nothing another session's play test depends on
    static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "tool-set-enabled-state",
        "unity-skill-generate",
    };

    static readonly JsonSerializerOptions FileJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly UnityLinkRegistry _registry;
    readonly McpClientTracker _clients;
    readonly IHubContext<FmpHub, IFmpUnityClient> _hub;
    readonly TimeProvider _time;
    readonly IOptions<MatrixOptions> _options;
    readonly ILogger<EditorLock> _logger;

    readonly object _gate = new();
    Holder? _holder;
    FileLock? _outside;
    (string SessionId, string Reason)? _lastDropped;
    EditorLockState _published = new();

    public EditorLock(
        UnityLinkRegistry registry,
        McpClientTracker clients,
        IHubContext<FmpHub, IFmpUnityClient> hub,
        TimeProvider time,
        IOptions<MatrixOptions> options,
        ILogger<EditorLock> logger)
    {
        _registry = registry;
        _clients = clients;
        _hub = hub;
        _time = time;
        _options = options;
        _logger = logger;
        clients.SessionEnded += OnSessionEnded;
    }

    sealed class Holder
    {
        public required string SessionId { get; init; }
        public required string Owner { get; set; }
        public required string Task { get; set; }
        public string ClientName { get; init; } = string.Empty;
        public string ClientVersion { get; init; } = string.Empty;
        public string? Root { get; init; }
        public DateTimeOffset Acquired { get; set; }
        public DateTimeOffset LastActivity { get; set; }
        public DateTimeOffset MirrorWritten { get; set; }
        public int InFlight { get; set; }
    }

    sealed record FileLock(string Owner, string Task, string Acquired, long Heartbeat, bool Matrix, string Session, DateTimeOffset Created);

    public static IReadOnlyList<string> ToolNames { get; } = [AcquireName, ReleaseName, StatusName];

    public static bool IsLockTool(string? name) => name is AcquireName or ReleaseName or StatusName;

    public static IEnumerable<Tool> Tools()
    {
        yield return new Tool
        {
            Name = AcquireName,
            Title = "Editor / Lock / Acquire",
            Description = "Claims the shared Unity editor for one burst of editor-changing work (a compile, one play session, an " +
                          "edit-mode batch). While you hold it, other sessions' editor-changing tools are refused; read-only tools " +
                          "still run for everyone. Answers 'busy' with who holds it when it is taken. Release with " +
                          $"{ReleaseName} as soon as the burst ends; a claim is freed on its own when your session closes or after " +
                          "a long idle stretch. Calling it again while you hold it starts a new claim.",
            InputSchema = Schema("""
                {"type":"object","properties":{
                  "task":{"type":"string","description":"What the claim is for, in a few words. Shown in the Unity editor."},
                  "owner":{"type":"string","description":"A label people recognize you by, e.g. 'claude-catwalk-score'. Defaults to your client name."}},
                 "required":["task"],"additionalProperties":false}
                """),
        };
        yield return new Tool
        {
            Name = ReleaseName,
            Title = "Editor / Lock / Release",
            Description = "Gives the shared Unity editor back after your burst: exit play mode and restore what you changed first. " +
                          "Only releases your own claim.",
            InputSchema = Schema("""{"type":"object","properties":{},"additionalProperties":false}"""),
            Annotations = new ToolAnnotations { IdempotentHint = true },
        };
        yield return new Tool
        {
            Name = StatusName,
            Title = "Editor / Lock / Status",
            Description = "Says who holds the shared Unity editor (an agent session or a lock file), " +
                          "for what, since when, and whether it is you.",
            InputSchema = Schema("""{"type":"object","properties":{},"additionalProperties":false}"""),
            Annotations = new ToolAnnotations { ReadOnlyHint = true, IdempotentHint = true },
        };
    }

    #region MCP tools

    public CallToolResult Call(CallToolRequestParams request, string? sessionId) => request.Name switch
    {
        AcquireName => Acquire(sessionId, Text(request.Arguments, "owner"), Text(request.Arguments, "task")),
        ReleaseName => Release(sessionId),
        _ => Status(sessionId),
    };

    public CallToolResult Acquire(string? sessionId, string? owner, string? task)
    {
        if (string.IsNullOrEmpty(sessionId))
            return Error("This connection has no MCP session id, so it cannot hold the editor lock.");
        task = Clip(task);
        if (task.Length == 0)
            return Error($"{AcquireName} needs 'task': what the claim is for, in a few words.");

        CallToolResult result;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            ExpireIfIdle(now);
            var link = _registry.GetLatestLink();
            var client = _clients.ClientOf(sessionId);
            owner = Clip(owner);
            if (owner.Length == 0)
                owner = string.IsNullOrEmpty(client?.Name) ? "agent" : client!.Name;

            if (_holder is { } held && held.SessionId == sessionId)
            {
                held.Owner = owner;
                held.Task = task;
                held.Acquired = now;
                held.LastActivity = now;
                WriteMirror(held, now);
                result = Ok($"acquired (a new claim; you already held it) for \"{task}\". {EditorLine(link)} {Reminder}");
            }
            else if (_holder is { } other)
            {
                result = Error($"busy: {Describe(other, now)} Wait and acquire again, or hand off (write a test case and stop).");
            }
            else if (link?.ProjectRoot is { } root && ReadLiveOutsideLock(root, now) is { } outside)
            {
                _outside = outside;
                result = Error($"busy: {DescribeOutside(outside, now)} Wait and acquire again, or hand off (write a test case and stop).");
            }
            else if (link is { Connected: true, EditorState: FmpEditorState.Playing or FmpEditorState.Paused })
            {
                result = Error("busy: the editor is in play mode and nobody holds the lock, so a person is playing (or a session " +
                               "left play mode on). Do not stop it; wait, or hand off.");
            }
            else
            {
                _holder = new Holder
                {
                    SessionId = sessionId,
                    Owner = owner,
                    Task = task,
                    ClientName = client?.Name ?? string.Empty,
                    ClientVersion = client?.Version ?? string.Empty,
                    Root = link?.ProjectRoot,
                    Acquired = now,
                    LastActivity = now,
                };
                _outside = null;
                if (_lastDropped?.SessionId == sessionId)
                    _lastDropped = null;
                WriteMirror(_holder, now);
                _logger.LogInformation("Editor lock acquired by {Owner} (session {SessionId}) for {Task}", owner, sessionId, task);
                result = Ok($"acquired for \"{task}\". {EditorLine(link)} {Reminder}");
            }
        }
        _ = PublishAsync();
        return result;
    }

    public CallToolResult Release(string? sessionId)
    {
        CallToolResult result;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            ExpireIfIdle(now);
            if (_holder is { } held && sessionId is not null && held.SessionId == sessionId)
            {
                Free($"released by {held.Owner}");
                result = Ok($"released after {Age(now - held.Acquired)}.");
            }
            else if (_lastDropped is { } dropped && dropped.SessionId == sessionId)
            {
                _lastDropped = null;
                result = Ok($"nothing to release: your claim was already freed ({dropped.Reason}). Acquire again before more editor work.");
            }
            else if (_holder is { } other)
            {
                result = Ok($"nothing to release: you do not hold the lock. {Describe(other, now)}");
            }
            else
            {
                result = Ok("nothing to release: nobody holds the lock.");
            }
        }
        _ = PublishAsync();
        return result;
    }

    public CallToolResult Status(string? sessionId)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            ExpireIfIdle(now);
            var link = _registry.GetLatestLink();
            if (_holder is { } held)
            {
                var mine = sessionId is not null && held.SessionId == sessionId;
                return Ok((mine ? $"you hold it: \"{held.Task}\" since {Clock(held.Acquired)} ({Age(now - held.Acquired)}). " : Describe(held, now) + " ") + EditorLine(link));
            }
            if (link?.ProjectRoot is { } root && ReadLiveOutsideLock(root, now) is { } outside)
                return Ok(DescribeOutside(outside, now) + " " + EditorLine(link));
            if (_lastDropped is { } dropped && dropped.SessionId == sessionId)
                return Ok($"free. Your last claim was freed ({dropped.Reason}). " + EditorLine(link));
            return Ok("free. " + EditorLine(link));
        }
    }

    #endregion

    #region Gate

    /// <summary>Why this session may not run the tool now, or null when it may.</summary>
    public string? Refusal(string? sessionId, string toolName)
    {
        if (!_options.Value.EnforceEditorLock || Exempt.Contains(toolName) || IsReadOnly(toolName))
            return null;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (ExpireIfIdle(now))
                _ = PublishAsync();
            if (_holder is not { } held || (sessionId is not null && held.SessionId == sessionId))
                return null;
            return $"Refused '{toolName}': {Describe(held, now)} Until it is released only read-only tools run for other " +
                   $"sessions. Wait and call {AcquireName}, or hand off (write a test case and stop).";
        }
    }

    /// <summary>Counts a Unity call of the holder as activity, so a long call never reads as idle.</summary>
    public IDisposable Track(string? sessionId)
    {
        lock (_gate)
        {
            if (_holder is not { } held || sessionId is null || held.SessionId != sessionId)
                return NoCall.Instance;
            held.InFlight++;
            held.LastActivity = _time.GetUtcNow();
            return new TrackedCall(this, held);
        }
    }

    bool IsReadOnly(string toolName)
        => _registry.GetLatestLink()?.Registry?.Tools
            .FirstOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase))?.ReadOnly == true;

    sealed class TrackedCall(EditorLock owner, Holder holder) : IDisposable
    {
        int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 1)
                return;
            lock (owner._gate)
            {
                holder.InFlight = Math.Max(0, holder.InFlight - 1);
                holder.LastActivity = owner._time.GetUtcNow();
            }
        }
    }

    sealed class NoCall : IDisposable
    {
        public static readonly NoCall Instance = new();
        public void Dispose() { }
    }

    #endregion

    #region Editor side

    /// <summary>Frees whatever holds the editor, including a lock file left by another session. Returns what was freed.</summary>
    public string ForceRelease()
    {
        string freed;
        lock (_gate)
        {
            if (_holder is { } held)
            {
                freed = $"the claim of {held.Owner}";
                Free("freed from the Unity editor by a person");
            }
            else if (_registry.GetLatestLink()?.ProjectRoot is { } root && ReadFile(root) is { } file)
            {
                freed = file.Owner.Length == 0 ? "the lock file" : $"the lock file of {file.Owner}";
                DeleteDirectory(root);
                _outside = null;
                _logger.LogWarning("Editor lock file of {Owner} removed from the Unity editor", file.Owner);
            }
            else
            {
                freed = "nothing";
            }
        }
        _ = PublishAsync();
        return freed;
    }

    void OnSessionEnded(string sessionId)
    {
        lock (_gate)
        {
            if (_holder is { } held && held.SessionId == sessionId)
                Free("its MCP session closed");
        }
        _ = PublishAsync();
    }

    #endregion

    #region State

    /// <summary>Periodic pass: frees idle claims, notices lock files, keeps the mirror fresh and tells the editor.</summary>
    public Task TickAsync()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            ExpireIfIdle(now);
            if (_holder is { } held)
            {
                if (now - held.MirrorWritten >= MirrorBeat)
                    WriteMirror(held, now);
            }
            else
            {
                var root = _registry.GetLatestLink()?.ProjectRoot;
                _outside = root is null ? null : ReadLiveOutsideLock(root, now);
            }
        }
        return PublishAsync();
    }

    public EditorLockState Snapshot()
    {
        lock (_gate)
        {
            if (_holder is { } held)
            {
                var idle = _options.Value.EditorLockIdleTimeout;
                return new EditorLockState
                {
                    Held = true,
                    Kind = KindAgent,
                    Owner = held.Owner,
                    Task = held.Task,
                    ClientName = held.ClientName,
                    ClientVersion = held.ClientVersion,
                    SessionId = held.SessionId,
                    AcquiredUnixMs = held.Acquired.ToUnixTimeMilliseconds(),
                    LastActivityUnixMs = held.LastActivity.ToUnixTimeMilliseconds(),
                    ExpiresUnixMs = held.InFlight == 0 ? (held.LastActivity + idle).ToUnixTimeMilliseconds() : 0,
                    CallsInFlight = held.InFlight,
                };
            }
            if (_outside is { } outside)
            {
                return new EditorLockState
                {
                    Held = true,
                    Kind = KindOutside,
                    Owner = outside.Owner,
                    Task = outside.Task,
                    AcquiredUnixMs = ParseAcquired(outside.Acquired)?.ToUnixTimeMilliseconds() ?? outside.Created.ToUnixTimeMilliseconds(),
                    LastActivityUnixMs = outside.Heartbeat > 0 ? outside.Heartbeat * 1000 : outside.Created.ToUnixTimeMilliseconds(),
                    ExpiresUnixMs = outside.Heartbeat > 0 ? (outside.Heartbeat + (long)FileTtl.TotalSeconds) * 1000 : 0,
                };
            }
            return new EditorLockState();
        }
    }

    /// <summary>Sends the current state to every linked editor that shows it, when it changed (or to one link, always).</summary>
    public async Task PublishAsync(UnityLink? only = null)
    {
        var state = Snapshot();
        if (only is null)
        {
            lock (_gate)
            {
                if (state == _published)
                    return;
                _published = state;
            }
        }
        foreach (var link in only is null ? _registry.Links : [only])
        {
            if (!link.Connected || !link.Features.Contains(FmpCapabilities.EditorLock))
                continue;
            try
            {
                await _hub.Clients.Client(link.ConnectionId).EditorLockChanged(state);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Failed to send the editor lock to {ProjectId}", link.ProjectId);
            }
        }
    }

    // caller holds _gate; true when it freed a claim
    bool ExpireIfIdle(DateTimeOffset now)
    {
        if (_holder is not { } held)
            return false;
        if (held.InFlight == 0 && now - held.LastActivity > _options.Value.EditorLockIdleTimeout)
        {
            Free($"no call for {Age(now - held.LastActivity)}");
            return true;
        }
        if (!_clients.IsOpen(held.SessionId))
        {
            Free("its MCP session closed");
            return true;
        }
        return false;
    }

    // caller holds _gate
    void Free(string reason)
    {
        if (_holder is not { } held)
            return;
        _holder = null;
        _lastDropped = (held.SessionId, reason);
        DeleteMirror(held);
        _logger.LogInformation("Editor lock of {Owner} ended: {Reason}", held.Owner, reason);
    }

    #endregion

    #region Lock file

    static string LockDirectory(string root) => Path.Combine(root, "Temp", "agent-editor-lock");

    // the lock file of a session outside the matrix, when it is live; a stale one, or a mirror the matrix no longer backs, is removed
    FileLock? ReadLiveOutsideLock(string root, DateTimeOffset now)
    {
        var file = ReadFile(root);
        if (file is null)
            return null;
        if (file.Matrix)
        {
            DeleteDirectory(root);
            return null;
        }
        var live = file.Heartbeat > 0
            ? now.ToUnixTimeSeconds() - file.Heartbeat <= (long)FileTtl.TotalSeconds
            : now - file.Created <= FileTtl;
        if (live)
            return file;
        // delete only if nobody beat in between
        if (ReadFile(root)?.Heartbeat == file.Heartbeat)
        {
            DeleteDirectory(root);
            _logger.LogInformation("Stale editor lock file of {Owner} removed", file.Owner);
        }
        return null;
    }

    FileLock? ReadFile(string root)
    {
        var directory = LockDirectory(root);
        try
        {
            if (!Directory.Exists(directory))
                return null;
            var created = new DateTimeOffset(Directory.GetCreationTimeUtc(directory), TimeSpan.Zero);
            var path = Path.Combine(directory, "lock.json");
            if (!File.Exists(path))
                return new FileLock(string.Empty, string.Empty, string.Empty, 0, false, string.Empty, created);
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var json = document.RootElement;
                return new FileLock(
                    Read(json, "owner"), Read(json, "task"), Read(json, "acquired"),
                    long.TryParse(Read(json, "heartbeat"), out var beat) ? beat : 0,
                    json.TryGetProperty("matrix", out var matrix) && matrix.ValueKind == JsonValueKind.True,
                    Read(json, "session"), created);
            }
            catch (JsonException)
            {
                // half-written: live while the directory is young
                return new FileLock(string.Empty, string.Empty, string.Empty, 0, false, string.Empty, created);
            }
        }
        catch (IOException e)
        {
            _logger.LogDebug(e, "Could not read the editor lock file under {Root}", root);
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            _logger.LogDebug(e, "Could not read the editor lock file under {Root}", root);
            return null;
        }
    }

    void WriteMirror(Holder holder, DateTimeOffset now)
    {
        holder.MirrorWritten = now;
        if (holder.Root is not { } root || !Directory.Exists(Path.Combine(root, "Temp")))
            return;
        try
        {
            var directory = LockDirectory(root);
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["owner"] = holder.Owner,
                ["task"] = holder.Task,
                ["acquired"] = holder.Acquired.ToOffset(_time.LocalTimeZone.GetUtcOffset(holder.Acquired)).ToString("yyyy-MM-ddTHH:mm:sszzz"),
                ["heartbeat"] = now.ToUnixTimeSeconds(),
                ["matrix"] = true,
                ["session"] = holder.SessionId,
                ["client"] = holder.ClientName,
            }, FileJson);
            File.WriteAllText(Path.Combine(directory, "lock.json"), json + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not write the editor lock file under {Root}", root);
        }
    }

    void DeleteMirror(Holder holder)
    {
        if (holder.Root is { } root && ReadFile(root) is { Matrix: true } file && file.Session == holder.SessionId)
            DeleteDirectory(root);
    }

    void DeleteDirectory(string root)
    {
        try
        {
            var directory = LockDirectory(root);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not remove the editor lock file under {Root}", root);
        }
    }

    static string Read(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value))
            return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty,
        };
    }

    static DateTimeOffset? ParseAcquired(string text)
        => DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value) ? value : null;

    #endregion

    #region Text

    const string Reminder = "Release it as soon as this burst ends (cap 15 minutes per claim); read-only tools never need it.";

    string Describe(Holder held, DateTimeOffset now)
    {
        var age = now - held.Acquired;
        var over = age > Cap ? ", past the 15-minute cap" : string.Empty;
        var client = $"{held.ClientName} {held.ClientVersion}".Trim();
        var busy = held.InFlight > 0 ? $"{held.InFlight} call(s) running" : $"last call {Age(now - held.LastActivity)} ago";
        return $"held by {held.Owner} ({(client.Length > 0 ? client + ", " : string.Empty)}session {Short(held.SessionId)}) for " +
               $"\"{held.Task}\" since {Clock(held.Acquired)} ({Age(age)}{over}; {busy}).";
    }

    string DescribeOutside(FileLock file, DateTimeOffset now)
    {
        var owner = file.Owner.Length == 0 ? "a session" : file.Owner;
        var task = file.Task.Length == 0 ? string.Empty : $" for \"{file.Task}\"";
        var beat = file.Heartbeat > 0 ? $", last heartbeat {Age(now - DateTimeOffset.FromUnixTimeSeconds(file.Heartbeat))} ago" : string.Empty;
        return $"held by {owner}{task} through the lock file Temp/agent-editor-lock (it does not claim through the matrix){beat}.";
    }

    static string EditorLine(UnityLink? link) => link switch
    {
        null => "No Unity editor is linked right now.",
        { Connected: false } => "Editor: reloading or reconnecting.",
        _ => link.EditorState switch
        {
            FmpEditorState.Compiling => "Editor: compiling; wait for it before playing.",
            FmpEditorState.Reloading => "Editor: reloading.",
            FmpEditorState.Playing => "Editor: in play mode.",
            FmpEditorState.Paused => "Editor: in play mode, paused.",
            _ => "Editor: ready.",
        },
    };

    string Clock(DateTimeOffset time) => time.ToOffset(_time.LocalTimeZone.GetUtcOffset(time)).ToString("HH:mm");

    static string Age(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds}s" : $"{span.Seconds}s";
    }

    static string Short(string sessionId) => sessionId.Length > 8 ? sessionId[..8] : sessionId;

    static string Clip(string? text)
    {
        text = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    static string? Text(IDictionary<string, JsonElement>? args, string name)
        => args is not null && args.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    static CallToolResult Ok(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    static CallToolResult Error(string text) => new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

    #endregion
}
