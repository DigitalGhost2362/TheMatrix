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

The package ships the server as `Server~/win-x64/server-payload.zip`, built from this folder:

```
dotnet publish src/Feeder.Matrix -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out
```

The zip holds the published `feeder-mcp-server.exe` renamed to `matrix-server.exe`,
`feeder-mcp-server.staticwebassets.endpoints.json`, `web.config` and a `version` file with the server
version. Change that version in `src/Feeder.Matrix/Feeder.Matrix.csproj`, in `src/Feeder.Matrix/Program.cs`
and in `McpServerManager.ServerVersion` together.

Shipped payload 0.3.0, `matrix-server.exe` SHA-256:
`cd334ea163403da418b022b899357a4d48e9b8ed3ff8d2b16572f2f6e4cf8a65`

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
