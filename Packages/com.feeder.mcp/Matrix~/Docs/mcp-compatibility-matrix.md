# MCP compatibility matrix — baseline vs Feeder Local Matrix

Baseline measured 2026-07-10 against the earlier server ("previous local"), Unity connected.

> Cloud baseline (`baseline-cloud.json`) is pending: no remote endpoint is reachable from this dev machine. The ≥50% control-plane SLO comparison will be run when cloud credentials are available; all other gates use previous-local numbers.

## Feature surface the matrix must reproduce

| MCP feature | Previous server local behavior (measured) | Matrix target |
|---|---|---|
| `initialize` | protocolVersion `2025-06-18`, capabilities: logging, prompts+listChanged, resources+listChanged, tools+listChanged; serverInfo `previous-server` | Same capability set; serverInfo `feeder-matrix`; negotiate `2025-03-26`…`2025-11-25` |
| Transport | Streamable HTTP (`Mcp-Session-Id` header, SSE responses), Kestrel, **binds 0.0.0.0** | Streamable HTTP + stdio shim, **127.0.0.1 only** |
| `ping` | Supported, p50 1.84 ms / p95 3.47 ms / p99 5.35 ms (10 000 iter, 0 errors) | ≤ same, SLO p95 ≤ 15 ms e2e incl. Unity |
| `tools/list` | 38 tools, 144 896 B response, **p50 118 ms / p95 173 ms** (no caching — regenerated per call) | Cached by registry hash: p95 ≤ 10 ms |
| `tools/call` | End-to-end Unity `ping` tool: p50 10.4 ms / p95 15.1 ms / p99 43.9 ms (500 iter, 0 errors) | Matrix routing overhead p95 ≤ 2 ms on top of Unity |
| `notifications/tools/list_changed` | Advertised | Advertised (`tools.listChanged`). Sent to every live session when the registry hash changes, when a link registers again after its tools were dropped, on unregister, and when a link stays down past `ReconnectGrace` |
| `prompts/list`, `prompts/get` | Advertised; currently returns empty list | Implement; empty list parity today |
| `resources/list`, `resources/read`, templates | Advertised; currently returns empty lists | Implement; empty list parity today |
| Session init | p50 4.0 ms / p95 8.3 ms (new session per iter) | ≤ baseline |
| Long-running tools | `script-update-or-create`, `package-add/remove`, `tests-run`, `assets-refresh` return Processing and complete after compile/domain reload (100 ms polling loop in the previous server) | Processing is reported as deferred and gets `DeferredToolTimeout`; time with the link down does not count; a tool may ask for more time (FMP section 8) |
| Error format | JSON-RPC error objects; tool failures as `isError: true` results | Byte-compatible mapping (FMP §4) |

## Tool inventory (25 exposed via `tools/list`)

assets-find-built-in, assets-material-create, assets-refresh, assets-prefab-close, assets-prefab-create, assets-prefab-instantiate, assets-prefab-open, assets-prefab-save, assets-shader-get-data, assets-shader-list-all, console-get-logs, gameobject-component-list-all, gameobject-duplicate, rql-mutate, rql-query, rql-schema, scene-create, scene-open, scene-save, scene-set-active, scene-unload, screenshot-isolated, script-execute, tests-run, unity-tool-list

Note: the Unity plugin registers more tools than are exposed (per-tool enable flags in the plugin config filter the list). Parity is defined against whatever the registry marks enabled, not this fixed set.

## Baseline environment behaviors to reproduce

- Unity compile / domain reload: in-flight deferred tools complete after reload via requestId; `tools/list` keeps answering.
- AI client disconnect: session dropped server-side; Unity link unaffected.
- Server restart: Unity plugin auto-reconnects; AI clients must re-initialize.
- Server process at rest: ~138 MB working set (matrix SLO: ≤ 80 MB matrix+shim).
