# Feeder Matrix Protocol v1 (FMP/1)

Status: Draft accepted for implementation — 2026-07-10

FMP is the private protocol between the **Feeder Unity Adapter** (inside the editor) and the **Feeder Local Matrix**. It is *not* MCP: MCP terminates at the matrix; FMP is our own contract, versioned independently of the MCP protocol.

- Transport: SignalR over WebSocket only, `http://127.0.0.1:<port>/fmp`.
- Wire format: MessagePack (SignalR MessagePack hub protocol).
- All messages share one envelope; binary payloads (screenshots, files) travel as raw `byte[]` fields — never base64 inside FMP.

## 1. Envelope

Every message carries:

| Field | Type | Notes |
|---|---|---|
| `protocolVersion` | string | `"1"`. Mismatch → `matrix.error` `FMP_VERSION_UNSUPPORTED` and disconnect. |
| `projectId` | string | Stable SHA-256/16 hex of normalized project path. |
| `unityInstanceId` | string | GUID per editor process (survives domain reload, not editor restart). |
| `sessionId` | string | Matrix-assigned link session (changes on reconnect). |
| `requestId` | string | Unique per request; responses echo it. |
| `traceId` | string | W3C trace-id for cross-process correlation. |
| `messageType` | string | See §2. |
| `deadlineUnixMs` | long? | Absolute deadline; 0/null = matrix default. |
| `payload` | msgpack map / bin | Message-specific body. |

## 2. Message types

| Type | Direction | Purpose |
|---|---|---|
| `unity.register` | U→B | Handshake (§3). Response: `sessionId`, accepted protocol version, resume info. |
| `unity.unregister` | U→B | Graceful shutdown (editor closing, not domain reload). |
| `registry.snapshot` | U→B | Full tool/prompt/resource registry + `registryHash`. Sent when hash differs from matrix's cached hash. |
| `registry.changed` | U→B | Delta (added/removed/updated entries) + new hash. |
| `tool.invoke` | B→U | Invoke tool: name, args (UTF-8 JSON bytes), `operationId`, deadline. |
| `tool.result` | U→B | Terminal result for `operationId`: success payload or error. May arrive on a *different* connection than the invoke (domain reload). |
| `tool.progress` | U→B | Progress notification for a running `operationId`. |
| `tool.cancel` | B→U | Cancel a queued/running `operationId`. |
| `resource.read` | B→U | Read resource by URI. Response is a `tool.result`-shaped completion. |
| `prompt.get` | B→U | Resolve prompt by name/args. |
| `heartbeat` | both | Liveness + editor state (`ready` / `compiling` / `reloading` / `playing` / `paused`). Interval 5 s, miss ×3 → link considered dead. |
| `matrix.error` | B→U | Protocol-level error (bad envelope, auth failure, backpressure). |

## 3. `unity.register` handshake payload

Unity MUST send:

- `projectPathHash` (= `projectId` derivation input check)
- `unityVersion` (e.g. `6000.0.32f1`)
- `pluginVersion`
- `supportedProtocolVersions` (array — matrix picks highest common)
- `toolRegistryHash` (matrix replies `registryUpToDate: true|false`; `false` triggers `registry.snapshot`)
- `authToken` (per-project 256-bit token, see ADR-0005)
- `editorState` (`ready` / `compiling` / `playing` / `reloading`)
- `pendingOperations` (operationIds it can still complete after a reload)

Matrix replies with `sessionId`, chosen protocol version, list of `operationId`s it still awaits (so Unity can resurrect or fail them), and scheduler config (queue capacity, heartbeat interval).

## 4. Error model

Payload of `matrix.error` and the error branch of `tool.result`:

| Field | Notes |
|---|---|
| `code` | Stable string: `FMP_VERSION_UNSUPPORTED`, `FMP_AUTH_FAILED`, `FMP_QUEUE_FULL`, `FMP_DEADLINE_EXCEEDED`, `FMP_CANCELLED`, `FMP_TOOL_NOT_FOUND`, `FMP_TOOL_FAILED`, `FMP_UNITY_UNAVAILABLE`, `FMP_INTERNAL` |
| `message` | Human-readable, no secrets |
| `retriable` | bool |
| `detailsJson` | Optional UTF-8 JSON bytes (tool stack traces etc.; stripped in release for MCP clients, kept in local logs) |

MCP mapping: `FMP_QUEUE_FULL` → JSON-RPC `-32000` server busy; `FMP_DEADLINE_EXCEEDED`/`FMP_CANCELLED` → request cancellation semantics; `FMP_TOOL_*` → `tools/call` result with `isError: true` (parity with current server); envelope-level failures → JSON-RPC error objects.

## 5. Link state machine (matrix view of a Unity instance)

```
        register ok                    heartbeat miss ×3 / ws close
Idle ──────────────► Ready ──────────────────────────────► Degraded
                      │ ▲                                      │
     editorState:     │ │ editorState: ready                   │ re-register within TTL (120 s)
     compiling/reload ▼ │                                      ▼
                     Busy ◄──────────────────────────── Resumed (replay pending op completions)
                                                               │ TTL expired
                                                               ▼
                                                          Evicted (pending ops → FMP_UNITY_UNAVAILABLE)
```

- `Busy` (compiling/reloading): matrix keeps accepting MCP requests, queues them (bounded), and answers `tools/list` from cache.
- `Degraded`: requests queue until TTL; new mutating requests fail fast with `FMP_UNITY_UNAVAILABLE` after 10 s.
- `Resumed`: same `unityInstanceId` re-registered; matrix re-delivers `tool.cancel`s and receives late `tool.result`s.

## 6. Versioning rules

- Additive fields: allowed within v1 (MessagePack maps, unknown keys ignored).
- Semantic/breaking changes: bump `protocolVersion`; matrix supports current and previous major for one release cycle.

## 7. Script compile (`Compile`, additive in v1)

Added 2026-10-06, see [ADR-0006](../adr/ADR-0006-script-compile-on-server.md). The matrix compiles C# for the editor's `script-execute` tool, so the Unity package ships no compiler. The editor loads the returned image and runs it; the matrix never loads or runs it.

**Capability.** `UnityRegisterResponse` gains `capabilities` (string array, MessagePack key 7, JSON `capabilities`). A matrix that serves `Compile` lists `"compile"`. A missing or empty array means the call is not available (older matrix): the editor must not call it and should report that the matrix is too old. Error replies (`FMP_VERSION_UNSUPPORTED`, `FMP_AUTH_FAILED`) carry an empty array. Older editors ignore the field.

**Call.** Hub method `Compile`, Unity to matrix, an invocation that returns a result:

```csharp
var response = await connection.InvokeAsync<CompileResponse>("Compile", request, cancellationToken);
```

Only a connection whose `Register` succeeded, and that has not unregistered or been replaced by a newer registration of the same project, is served. Any other caller gets `error` set and nothing is compiled. SignalR runs one hub call per connection at a time, so later messages from the same link (for example `CompleteTool`) wait while its compile runs. The editor mirrors both DTOs as plain classes with the same property names, like the other DTOs in `Editor/Scripts/Matrix/FeederMatrixMessages.cs`.

`CompileRequest`

| Field | Key | Type | Notes |
|---|---|---|---|
| `source` | 0 | string | Required. One C# compilation unit, at most 5,242,880 characters. |
| `referencePaths` | 1 | string[] | Paths of the assemblies to reference, at most 2,000. The editor sends every loaded, non-dynamic assembly that has a file. Missing or unreadable files are skipped. A second path to the same file, a second strong-named assembly with the same identity, or a second weak-named assembly with the same simple name is ignored (the first one wins), so duplicates never fail the compile. |
| `assemblyName` | 2 | string | Default `DynamicAssembly`. |
| `syntaxOnly` | 3 | bool | Parse only: no reference is read and no image is produced. Replaces the editor's local syntax check. |
| `timeoutMs` | 4 | int | 0 = matrix default (60 s). Covers queueing and compiling. |

`CompileResponse`

| Field | Key | Type | Notes |
|---|---|---|---|
| `success` | 0 | bool | |
| `assembly` | 1 | bytes (base64 in JSON) | PE image, no PDB. Null on failure and for `syntaxOnly`. |
| `diagnostics` | 2 | string[] | Failed compile: every diagnostic of the emit result, errors and warnings, in emit order, each formatted by Roslyn's `Diagnostic.ToString()`, for example `(16,9): error CS0127: ...` (1-based line and column inside `source`). When reference paths were skipped, one last line gives their count. `syntaxOnly`: syntax errors only. Success: empty. |
| `error` | 3 | string | Set when nothing could be compiled: link not registered, a limit exceeded, queue full, timeout, internal failure. Null otherwise. |

Compiler: Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.6.0), `LanguageVersion.CSharp12`, `OutputKind.DynamicallyLinkedLibrary`, every other compilation option at its default (debug, AnyCPU, unsafe off, nullable off), no preprocessor symbols. These are the settings the package used with its bundled 4.8 compiler; the diagnostic format is unchanged, and the texts and order compared against 4.8 (CS0127, CS1525, CS0103, CS1002, CS0168, CS0219) match exactly.

Limits (configuration section `FeederMatrix`): `CompileConcurrency` 2 compiles run at once, `CompileQueueCapacity` 16 more may wait (beyond that `error` asks to retry), `CompileTimeout` 00:01:00, `CompileMaxSourceChars` 5242880, `CompileMaxReferences` 2000. Closing the connection cancels its compile. Reference metadata is cached per path and reloaded when the file's last write time or length changes; files are read into memory and never kept locked.

JSON payloads (hub protocol `json`, camelCase):

```json
{"source":"public static class Script { public static int Main() => 42; }","referencePaths":["C:\\...\\mscorlib.dll"],"assemblyName":"DynamicAssembly","syntaxOnly":false,"timeoutMs":0}
{"success":true,"assembly":"TVqQAAMAAAAEAAAA...","diagnostics":[],"error":null}
{"success":false,"assembly":null,"diagnostics":["(5,17): error CS0103: The name 'missing' does not exist in the current context"],"error":null}
```

## 8. Editor health and deadlines (additive in v1)

Added 2026-10-15. Every field below is a new key; a side that does not know it ignores it.

**Heartbeat.** The editor sends `Heartbeat` every 2 s from a pool thread, so it keeps arriving while the editor's main thread is held. A matrix that sees no heartbeat from a link treats that plugin as older and reports its health as unknown.

| Field | Key | Type | Notes |
|---|---|---|---|
| `lastMainTickUnixMs` | 2 | long | Last time the main thread ran an editor update. |
| `updating` | 3 | bool | `EditorApplication.isUpdating` (asset import). |
| `focused` | 4 | bool | The editor window has focus (an unfocused editor ticks slower, which is not a hang). |
| `inFlight` | 5 | `InFlightOperation[]` | `operationId` (0), `toolName` (1), `startedUnixMs` (2) of every call the editor is running. |
| `processId` | 6 | int | The editor process, read by the matrix for its windows and to tell a reload from an exit. |
| `consoleLogPath` | 7 | string | `Application.consoleLogPath` of this editor. |

Right before a domain reload the editor sends one more heartbeat with `editorState` = `reloading`, so a link that drops next reads as reloading rather than gone.

**Register.** `UnityRegisterRequest` gains `projectRoot` (key 11, the project folder) and `mcpEntryName` (key 12, the name agents know the matrix by in their MCP config). A register that carries `projectRoot` also tells the matrix that the plugin reports deferral, so operations it lost in a reload are answered at once with `FMP_INTERRUPTED` instead of at their deadline.

**Progress.** `ToolProgress` gains `deferred` (key 3) and `extendDeadlineMs` (key 4). `deferred` means the tool answered Processing and completes after a compile or domain reload; the matrix moves the call's deadline to `DeferredToolTimeout` (default 15 minutes). `extendDeadlineMs` asks for that much more time from now. Neither moves a deadline past `MaxToolTimeout` (default 30 minutes) after the call started.

**Waiting for a link.** A call that finds the project's link down waits for it to come back for up to `ReconnectGrace` (default 180 s); time spent with the link down does not count against the call's deadline. A project that never linked, a link down longer than the grace, or an editor process that has exited fails the call at once. Every timeout and lost-link error ends with the matrix's editor health summary.

**Error code.** `FMP_INTERRUPTED`: the operation was cut off by a domain reload before it finished; its code may have run in part.

## 9. Editor lock (additive in v1)

Added 2026-10-22. The matrix keeps one claim on the editor, held by an MCP session (the
`editor-lock-*` MCP tools). While it is held, the matrix refuses other
MCP sessions' calls to Unity tools whose `ToolDescriptor.readOnly` is false.

**Capability and feature.** A matrix that keeps the lock lists `"editor-lock"` in
`UnityRegisterResponse.capabilities`. `UnityRegisterRequest` gains `features` (key 13, string array); an
editor that lists `"editor-lock"` accepts `EditorLockChanged`. The matrix never sends it to an editor
that did not list the feature, and an editor never calls the hub methods below on a matrix without the
capability.

**`EditorLockChanged(EditorLockState)`** (B->U). Sent right after `Register` and whenever the state
changes (at most every 5 s for the holder's activity and the lock file).

| Field | Key | Type | Notes |
|---|---|---|---|
| `held` | 0 | bool | |
| `kind` | 1 | string | `agent` (an MCP session) or `outside` (a live `Temp/agent-editor-lock/lock.json` written by a session that does not claim through the matrix). |
| `owner` | 2 | string | Label the holder chose. |
| `task` | 3 | string | |
| `clientName` | 4 | string | MCP `clientInfo.name` of the holding session. |
| `clientVersion` | 5 | string | |
| `sessionId` | 6 | string | MCP session id; empty unless `kind` is `agent`. |
| `acquiredUnixMs` | 7 | long | Start of the current claim. |
| `lastActivityUnixMs` | 8 | long | Last tool call of the holder, or the lock file's heartbeat. |
| `expiresUnixMs` | 9 | long | When an idle claim is freed; 0 = not while it lasts. |
| `callsInFlight` | 10 | int | Holder's calls still running. |

**`ReleaseEditorLock() -> string`** (U->B). A person frees whatever holds the editor, including an
`outside` lock file; returns what was freed. Answers only a registered link.

**Lifetime.** An agent claim ends on `editor-lock-release`, when its MCP session ends, or after
`EditorLockIdleTimeout` (default 10 minutes) with no call from it and none running, or when a person
frees it from the editor. The matrix mirrors every claim to
`Temp/agent-editor-lock/lock.json` (with `"matrix": true`), so sessions using the lock-file protocol see
it.
