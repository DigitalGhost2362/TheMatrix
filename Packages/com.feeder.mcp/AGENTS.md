# Contributing to Matrix Bridge

Notes for people and coding agents who change this package. Users read `README.md`; this file is about the code.

## Data flow

```
AI client  --MCP (Streamable HTTP, or stdio through Feeder.StdioShim)-->  Matrix server (.NET, Matrix~/src/Feeder.Matrix)
Matrix server  --FMP/1 (SignalR + MessagePack, loopback)-->  Unity Editor plugin (Editor/, Feeder.MCP.Editor)
```

- The server is a separate, persistent process. It outlives Unity domain reloads and answers `editor-health`,
  the editor lock tools and `tool-call` even when Unity is hung.
- Every Unity tool call travels `Program.cs` call handler -> `ServerTools.CallUnityAsync` (editor lock gate) ->
  `IUnityBackend` -> FMP `InvokeTool` -> the Editor's tool host -> your `[AiTool]` method -> result back the same way.
- The Unity tool list is sent to the server on connect (registry hash). The server only forwards; it does not know
  what a tool does, except its `ReadOnlyHint` (see "Editor lock" below).
- MCP resources (`matrix://...`) are read by the server from the link state or by forwarding a read-only `rql-query`.
- Server code: `Matrix~/src/Feeder.Matrix` (MCP, `Tools/`, `Backends/`, `Unity/` hub and link registry),
  `Matrix~/src/Feeder.Matrix.Protocol` (FMP DTOs). Protocol notes: `Matrix~/Docs/protocol/`, decisions: `Matrix~/Docs/adr/`.

## Adding a tool

1. Put a partial class with `[AiToolType]` under `Editor/Scripts/API/Tool/` (one file per tool or tool family, named
   like its id: `Editor.Undo.cs`, `Tests.Query.cs`, `Batch.Execute.cs` are recent examples).
2. Add a method with `[AiTool(id, Title = "Group / Name", Enabled = false, ReadOnlyHint = ...)]`.
   - New tools start with `Enabled = false`; people turn them on in the Skills window.
   - Set `ReadOnlyHint = true` for tools that change nothing in the Editor or on disk. The server editor lock treats
     every tool without it as Editor-changing and makes other sessions wait for it.
   - The first segment of `Title` picks the Skills window group (`Editor/Scripts/UI/Window/SkillCatalog.cs`).
3. Describe it: `[Description]` on the method and on every parameter, `[AiSkillDescription]` (one or two sentences, it
   becomes the skill's front matter) and `[AiSkillBody]` (the how-to). Descriptions cost tokens in every session: keep
   them short and concrete.
4. Return a data model for structured output, or a `ResponseCallTool`; report problems with `ResponseCallTool.Error` or
   the tagged style `[code retry_after_ms=N] message` (see `Editor/Scripts/API/Tool/Tests/TestRunGuard.cs`).
5. Record Undo for anything that changes scenes or assets, where Unity supports it.
6. Every new `.cs` file and folder needs a `.meta` with a fresh random 32-hex guid. Folders ending in `~` need none.
7. Add an EditMode test under `Tests/Editor/` (assembly `Feeder.MCP.Editor.Tests`).

## Traps

- **Main thread.** Tool methods run on a worker thread. Every Unity API call must go through
  `MainThread.Instance.Run(() => ...)`. Never block the main thread waiting on another thread that needs it.
- **Domain reload.** A script change or entering play mode reloads the AppDomain: static fields, running tasks and
  event subscriptions vanish, and the server link drops and returns. Anything that must survive (a test run, a build,
  a bake) is a job stored on disk under `Library/` (see `Editor/Scripts/API/Tool/Tests/TestRunStore.cs`) that the tool starts, then reports
  as `processing`; the caller polls a status tool. Do not keep job state only in memory.
- **Long calls.** Clients time out after tens of seconds. Start long work as a job and return at once, or return in
  slices. Do not hold a tool call for minutes.
- **Editor lock.** Editor-changing tools from a session without the lock get an implicit claim, or wait in the queue
  when someone else holds it. Never read the lock from tool code; the server does it. Read-only tools never wait.
- **Play mode.** Do not enter or leave play mode from a tool unless that is the tool's purpose, and never leave it on.
- **Tests must not leave state behind.** Temp scenes unsaved and closed, temp assets under `Assets/_MxTest*` deleted,
  prefs restored. Never touch `TestRunGuard` or `TestResultCollector.TestCallRequestID` of a live run, and never save
  the open scene.
- **Skill text is code.** Skill markdown lives in `Editor/Scripts/Skills/` and tool attributes; the generated
  `.claude/skills/*/SKILL.md` files are output of `unity-skill-generate`, not hand-written.

## Packaging the server payload

The package ships the server as `Server~/<rid>/server-payload.zip`, built from `Matrix~`. The steps and the payload
layout are in `Matrix~/README.md` ("Rebuild the payload"). Bump the server version in three places together:
`src/Feeder.Matrix/Feeder.Matrix.csproj`, `src/Feeder.Matrix/Program.cs` (`MatrixVersion.Value`) and
`McpServerManager.ServerVersion`. Zip first, then bump `ServerVersion`, so an Editor never expects a payload that does
not exist yet. Do not commit a payload zip or a version bump without a review.

## Tests and checks

Server (from `Matrix~`, .NET SDK pinned in `global.json`):

```
dotnet test tests/Feeder.Matrix.UnitTests         -p:NuGetAudit=false
dotnet test tests/Feeder.Matrix.IntegrationTests  -p:NuGetAudit=false
dotnet test tests/Feeder.Matrix.ProtocolTests     -p:NuGetAudit=false
dotnet test tests/Feeder.Matrix.CompileTests      -p:NuGetAudit=false
git checkout -- '*packages.lock.json'
```

The last line undoes lock file changes the restore makes. Run the projects one after another: they share `obj/`.

Editor: EditMode tests run from the Unity Test Runner (assembly `Feeder.MCP.Editor.Tests`) or through the `tests-run`
tool. They need every open scene saved. Without an Editor you can only compile-check: build copies of
`Feeder.MCP.Editor.csproj` and its Tests csproj that add the new files, into a scratch output folder, and delete the copies.

## Style

- Minimal code that follows the surrounding patterns; reuse an existing helper before adding a new one.
- Comments say why, not what, and are rare. ASCII punctuation in docs and comments.
- Do not name other projects or their authors in code, tests, docs or commit messages.
- Do not push, tag or publish without a review.
