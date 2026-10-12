# ADR-0001: Runtime and technology stack

Status: Accepted — 2026-07-10

## Context

Feeder Local Matrix replaces the previous server executable with a matrix we own, running fully local with lower latency. This phase swaps the server only; the Unity-side tool implementations stay as-is.

## Decision

| Component | Choice | Notes |
|---|---|---|
| Matrix runtime | .NET 10 LTS (`net10.0`) | LTS until Nov 2028. SDK 10.0.301 pinned via `global.json`. |
| MCP implementation | Official MCP C# SDK (`ModelContextProtocol.*`) | Version pinned in `Directory.Packages.props`; NuGet lock files committed. |
| MCP HTTP transport | Streamable HTTP on `127.0.0.1` only | Never `0.0.0.0` (fixes the previous server's exposure). |
| MCP stdio transport | `Feeder.StdioShim` — tiny forwarder process | Newline-delimited JSON-RPC on stdio, forwards to the persistent matrix; zero business logic. |
| Unity ↔ Matrix link | SignalR, WebSocket-only (no negotiate fallback, no long polling) | Keeps reconnect/RPC abstraction the Unity plugin already uses. |
| Internal serialization | MessagePack on the Unity link | Smaller frames, no base64 for binary payloads. |
| MCP edge serialization | System.Text.Json, UTF-8, source-generated contexts on hot paths | |
| Logging | stderr + rolling file under `Library/FeederMatrix/logs` | **No byte ever written to MCP stdout other than JSON-RPC.** |
| Distribution (win-x64) | Self-contained; Native AOT evaluated after Phase 9 benchmarks | AOT adopted only if MCP SDK + SignalR test suites pass, startup/memory improve measurably, and no reflection-based feature is lost. |
| macOS / Linux | Deferred | Layout keeps RID-specific publish dirs. |

## Why an out-of-process matrix

Unity domain reload kills any in-editor endpoint mid-request. A persistent external process keeps MCP sessions (and AI clients) alive across compile/reload/play-mode transitions; Unity reconnects to the matrix, not the other way around.

## Consequences

- Two processes to ship (matrix + shim) plus the Unity adapter.
- Protocol between Unity and matrix is ours to version (see [feeder-matrix-protocol-v1.md](../protocol/feeder-matrix-protocol-v1.md)).
