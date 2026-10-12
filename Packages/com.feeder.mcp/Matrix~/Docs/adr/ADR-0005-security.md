# ADR-0005: Local security model

Status: Accepted — 2026-07-10

The previous server binds `0.0.0.0` — any LAN peer can reach the MCP endpoint. The matrix fixes this class of issue by design:

## Mandatory requirements (Phase 8 gate)

1. Bind **`127.0.0.1` only** — both the MCP HTTP endpoint and the SignalR hub. Never `0.0.0.0`/`::`.
2. **256-bit random token per project**, generated on first registration, stored under `Library/FeederMatrix/token` with restrictive ACLs (owner-only). Unity presents it in the registration handshake; MCP clients present it via header when project binding requires it.
3. Token comparison is **constant-time**; tokens never appear in logs, error messages, or crash dumps.
4. Validate `Host` and `Origin` headers on every HTTP/WebSocket request (DNS-rebinding defense). Reject anything that is not `127.0.0.1`/`localhost` with the expected port.
5. Payload size limit (default 16 MB), per-session request rate limit, and per-tool timeout limit.
6. Duplicate JSON-RPC request IDs within a session are rejected.
7. Project isolation: a request bound to project A can never be routed to project B's Unity instance.
8. No CORS wildcard on any endpoint; detailed exception bodies disabled in release builds (generic error + correlation ID; details go to the local log only).

## Non-goals

- TLS on loopback (no benefit against a local attacker who can already read the token file).
- Multi-user machines with hostile local users are out of threat model for v1 (file ACLs are the mitigation).
