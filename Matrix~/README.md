# Feeder Local Matrix

Feeder-owned persistent MCP server for Matrix Bridge: MCP (Streamable HTTP or unified stdio
mode) on the AI side and SignalR/JSON (FMP/1) on the Unity side. Runs entirely on loopback. The
published `feeder-mcp-server.exe` ships in the package payload renamed to `matrix-server.exe`; the
Unity package looks for that name.

Docs: [ADRs](Docs/adr/) · [FMP/1 protocol](Docs/protocol/feeder-matrix-protocol-v1.md) · [Compatibility matrix](Docs/mcp-compatibility-matrix.md)

## Layout

```
src/Feeder.Matrix/           ASP.NET Core matrix: MCP endpoint (/mcp), FMP hub (/fmp), /healthz, C# compile for script-execute
src/Feeder.Matrix.Protocol/  FMP/1 DTOs (MessagePack)
src/Feeder.StdioShim/        stdio ⇄ Streamable HTTP relay for stdio-only MCP clients
tests/                       unit / protocol / integration (xunit)
benchmarks/                  feeder-matrix-bench — MCP latency harness (also used for Phase 0/10 gates)
Benchmarks/                  recorded baselines + exported tool definitions
```

## Build & test

Requires .NET SDK 10.0.3xx (pinned in `global.json`).

```
dotnet build Feeder.Matrix.slnx
dotnet test  Feeder.Matrix.slnx
```

## Rebuild the payload

The package ships the server as `Server~/<rid>/server-payload.zip` for `win-x64`, `osx-x64`, `osx-arm64` and
`linux-x64`. Build them all, or some with `-Rids`:

```
pwsh -File tools/publish-payloads.ps1 [-Rids osx-arm64,linux-x64]
```

Each zip holds the published `feeder-mcp-server[.exe]` renamed to `matrix-server[.exe]`,
`feeder-mcp-server.staticwebassets.endpoints.json`, `web.config` (Windows only) and a `version` file with the server
version. Change that version in `src/Feeder.Matrix/Feeder.Matrix.csproj`, in `src/Feeder.Matrix/Program.cs`
and in `McpServerManager.ServerVersion` together, then run the script and put the printed hashes below.

Shipped payload 0.10.0, `matrix-server` SHA-256:

| rid | SHA-256 |
| --- | --- |
| win-x64 | `76254cda5520f6a302e7dc0644ac010ca8b9a7e5903ccccff65bce1ebfac5590` |
| osx-x64 | `a893c183a30fa4edebfbdfce0c3501811c3e3d32f633dfd54bcf218918afc601` |
| osx-arm64 | `4f67eed2ff04337c0aee397ee5f10d03c2f04a20ac2de8e1fff88c2e064dcf87` |
| linux-x64 | `91fa8b11424993e602b0af5e6ee5427c1206ab196c1dd9a28f83cb279324532a` |

## Run (matrix-only, mock backend)

```
$env:FeederMatrix__EnableMockBackend = 'true'
dotnet run --project src/Feeder.Matrix          # 127.0.0.1:20270
```

MCP endpoint: `http://127.0.0.1:20270/mcp` · health: `/healthz` · stdio clients launch the same
executable with `client-transport=stdio port=20270`; it auto-starts the persistent HTTP mode.

## Benchmark

```
feeder-matrix-bench baseline --url http://127.0.0.1:20270/mcp --label X --out out.json [--export dir] [--pid N]
```

Measured 2026-07-10 (same machine, 0 errors on every case):

| Case (p50/p95 ms) | Earlier server (local) | Feeder matrix (mock) |
|---|---|---|
| ping warm ×10k | 1.84 / 3.47 | **0.27 / 0.44** |
| tools/list ×1k | 117.7 / 172.9 (145 KB) | 0.27 / 0.44 (mock; parity target ≤10 ms cached) |
| tools/call ping ×N | 10.4 / 15.1 (via Unity) | 0.25 / 0.40 (mock — Unity link lands in Phase 4) |
| session initialize ×50 | 4.0 / 8.3 | 0.85 / 1.33 |

The mock numbers bound matrix routing overhead (SLO p95 ≤ 2 ms — met). End-to-end comparison
against a live Unity editor is the Phase 10 gate.

## Status

- [x] Phase 0 — baseline + definition export (`Benchmarks/`)
- [x] Phase 1 — ADRs + FMP/1 spec (`Docs/`)
- [x] Phase 2 — solution scaffold (net10.0, nullable, warnings-as-errors, lock files)
- [x] Phase 3 — MCP MVP: initialize, tools/list, tools/call, prompts/resources (empty parity), ping, sessions, stdio shim, health, Serilog stderr+file, loopback+Host validation; 10k-ping soak clean
- [x] Phase 4 — Unity adapter (live registry publication, tool routing, reconnect, deferred completion)
- [ ] Phase 5–10 — scheduler, parity+cache, pending ops, security, tuning, migration
