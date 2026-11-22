# ADR-0003: Project routing, execution scheduling, and backpressure

Status: Accepted — 2026-07-10

## Routing

- One matrix serves **multiple Unity projects**. Each Unity editor registers with a `projectId` (stable hash of the project path) and a `unityInstanceId` (per-editor-process GUID).
- **Default: one active editor instance per project.** If a second instance registers for the same `projectId`, the newer registration wins and the older link is marked stale (behavior parity with the previous server); a future flag may allow explicit multi-instance addressing.
- MCP requests are routed to a project by the client's session binding (a session is bound to a project at connect time via endpoint path or configuration). Cross-project requests are rejected — project A must never receive project B's traffic.

## Execution scheduling (Unity side is single-threaded)

The repo has 77+ tool files that marshal onto the Unity main thread via `MainThread.Instance`. Scheduling rules:

1. **One bounded channel per project**, default capacity **32** requests.
2. **At most one mutating request executes at a time** per project (serialized).
3. Read-only requests may run in parallel **only** if the tool declares `ThreadSafe=true` metadata; otherwise they queue like mutating ones.
4. Deadline (`deadlineUnixMs`) is checked **before enqueue** and **again before execute**; an expired request is completed with a timeout error and never reaches Unity.
5. Client cancellation removes a not-yet-started request from the queue immediately.
6. When the queue is full, the matrix answers with an MCP `server busy` error (JSON-RPC `-32000`, `retriable: true` in data) instead of holding the request indefinitely.

## No polling

The current plugin completes deferred tool results via a 100 ms polling loop (`NotifyToolRequestCompleted`). The matrix replaces every polling wait with event-driven completion (`TaskCompletionSource` keyed by `requestId`/`operationId`).

## Consequences

- Queue capacity and parallelism are per-project config knobs with safe defaults.
- Tool metadata gains `ThreadSafe`, `ReadOnly`, `Destructive`, `Idempotent` annotations (Phase 6) — absent metadata is treated as mutating/not-thread-safe (conservative).
