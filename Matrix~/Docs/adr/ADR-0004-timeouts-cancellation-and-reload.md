# ADR-0004: Timeouts, cancellation, and domain-reload survival

Status: Accepted — 2026-07-10

## End-to-end cancellation chain

`AI client → matrix → Unity` carries one logical cancellation:

- MCP `notifications/cancelled` (or HTTP connection drop / stdio close) cancels the matrix-side `CancellationTokenSource` for that `requestId`.
- The matrix forwards `tool.cancel` to Unity; the Unity adapter signals the tool's token and, if the request is still queued, drops it.
- Every hop enforces `deadlineUnixMs`; the matrix sets it from per-tool timeout config (default 60 s control-plane, tool-specific overrides for compile/test/import which may run minutes).

A request past its deadline must not keep mutating Unity: the adapter checks the deadline before invoking the tool; already-running tools get a best-effort token cancellation.

## Long-running operations and domain reload

Tools like script create/update, package add/remove, and test runs trigger recompiles/domain reloads and complete *after* the reload. Design (implemented Phase 7):

- `PendingOperationStore` in the **matrix** keyed by `operationId` — independent of any SignalR connection ID.
- Unity persists minimal operation state under `Library/FeederMatrix/pending-ops/` so the post-reload domain can report completion.
- The matrix holds the MCP request open (session-scoped) and resolves it when Unity reports `tool.result` for the `operationId`; progress flows as `tool.progress` notifications.
- Cancel works both before and after the reload boundary.
- Orphaned operations expire via TTL (default 10 min) and complete with a explicit timeout error — no request is left hanging.

## Reconnect targets

- Matrix restart → clients reconnect ≤ 1 s (shim/HTTP retry with backoff).
- Unity domain reload → Unity link re-established ≤ 2 s (exponential backoff with jitter, floor 100 ms).
- Neither event may require restarting the AI client or the Unity editor.
