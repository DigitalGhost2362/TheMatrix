# ADR-0002: MCP version baseline and transports

Status: Accepted — 2026-07-10

## Decision

1. **MCP spec baseline: `2025-11-25`, with mandatory version negotiation.** The matrix advertises its preferred version at `initialize` and accepts any mutually supported version ≥ `2025-03-26`. The previous server negotiates `2025-06-18` (measured in Phase 0 baseline); clients on that version must keep working unchanged.
2. **Two client transports, one session model:**
   - *Streamable HTTP* at `http://127.0.0.1:<port>/mcp` — the primary transport. Session identified by `Mcp-Session-Id` header.
   - *stdio* via `Feeder.StdioShim` for clients that only speak stdio. The shim owns nothing but framing: it relays newline-delimited JSON-RPC between its stdio and the persistent matrix over local IPC, and exits when its stdin closes.
3. **One persistent matrix serves all clients and all Unity projects.** Client processes never spawn a full MCP server each; the shim connects to the already-running matrix (starting it on demand if absent, with a lock to prevent races).
4. **MCP Tasks (experimental in 2025-11-25) are not used for long-running operations yet.** The matrix reproduces the current plugin's `Processing`/deferred-result behavior (Phase 7) and can map onto MCP Tasks once client support stabilizes.

## Session lifecycle

- `initialize` → capability negotiation → `notifications/initialized`.
- Sessions survive Unity domain reloads (matrix-side state).
- Sessions die with the client connection or on explicit `DELETE` (HTTP) / stdin close (stdio).
- Duplicate request IDs within a session are rejected (`-32600`).

## Consequences

- The stdio shim must be tiny and start in well under the 1 s cold-start SLO.
- Matrix upgrade requires draining or dropping active sessions; clients are expected to re-initialize on reconnect.
