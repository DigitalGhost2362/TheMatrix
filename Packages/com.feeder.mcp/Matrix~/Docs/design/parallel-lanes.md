# Design: Parallel Lanes — multiple agents developing features concurrently against one Unity Editor

Status: Draft — 2026-07-25
Related: ADR-0003 (routing & scheduling), ADR-0004 (timeouts, cancellation, reload)

## 1. Problem

The user runs N terminals (or N Matrix Space panes), each an autonomous agent building a
different game feature. Example:

- **Lane `ui`** — builds the Shop UI, needs Game View screenshots to check layout.
- **Lane `combat`** — implements a damage system, needs play mode + screenshots to verify.

Both drive the same Unity Editor through MCP. Today they silently destroy each other.

## 2. What actually conflicts

A Unity Editor has exactly **one** of each global below. This is the hard constraint — no amount
of protocol design makes two play mode sessions coexist in one Editor process.

| Global resource | Owner today | Failure when two lanes share it |
| --- | --- | --- |
| Script write → compile → domain reload | any lane, unguarded | reload kills the other lane's play mode, wipes statics, drops in-flight ops |
| Play mode session | `EditorApplication.isPlaying` | lane B's `isPlaying=false` aborts lane A's verification mid-run |
| Active scene / `scene-open` | any lane | lane B's `scene-open` single-mode destroys lane A's scene setup |
| Prefab edit stage | one stage | `assets-prefab-open` from B closes A's stage |
| Game View render texture | one window | `screenshot-game-view` returns whoever's frame is playing — wrong lane's image |
| Test runner | one run | second `tests-run` fails or interleaves |
| `Selection` | one selection | mild; breaks `ui-inspect-pick` |
| Console log stream | global | lane A sees lane B's exceptions and "fixes" the wrong bug |
| Asset writes in disjoint folders | per-path | safe **if** the folders really are disjoint |
| Pure reads (`*-find`, `*-get-data`) | — | safe |
| `screenshot-isolated` / `screenshot-camera` | transient global (layers, lights) | needs a short lock, not a long lease |

### 2.1 Gaps in the current implementation

- [`UnityLinkRegistry`](../../src/Feeder.Matrix/Unity/UnityLinkRegistry.cs) keys links by
  `projectId` only, newest-registration-wins. A second Editor on the same project **evicts** the
  first. Multi-instance is unaddressable.
- [`BackendRouter.Resolve()`](../../src/Feeder.Matrix/Backends/BackendRouter.cs) returns
  `GetDefaultLink()` — the first connected link. There is no per-MCP-client routing.
- ADR-0003 serializes **one tool call** at a time per project. That is not enough: a verification
  is a *multi-call session* (`set-state true` → `screenshot` → `screenshot` → `set-state false`)
  and other lanes interleave freely inside it.
- [`Editor.Application.SetState`](../../../Editor/Scripts/API/Tool/Editor.Application.SetState.cs)
  writes `EditorApplication.isPlaying` with no ownership check.
- [`MatrixSpaceReloadCoordinator`](../../../Editor/Scripts/MatrixSpace/MatrixSpaceReloadCoordinator.cs)
  already implements the exact lease + refcount + `LockReloadAssemblies` pattern we need — but only
  for in-Editor Matrix Space panes. External MCP clients bypass it entirely.
- `McpClientTracker` has a `sessionId` per MCP client but no notion of *identity* — every Claude
  Code terminal reports `ClientInfo.Name == "claude-code"`, so lanes are indistinguishable.

## 3. Design overview

Three layers. Each is independently useful; ship them in order.

```
  terminal 1 ──┐                    ┌─ Layer 0: Lane identity (who is asking)
  terminal 2 ──┼─→ Matrix server ──→├─ Layer 1: Arbiter — leases on Editor globals
  pane 3     ──┘   (BackendRouter)  ├─ Layer 2: Verify job queue — batch play mode runs
                                    └─ Layer 3: Editor pool — real parallelism (opt-in)
```

**The central idea of Layer 2:** stop letting agents *drive* play mode. Let them *submit a
verification job* and continue working. The Arbiter batches all queued jobs into **one** compile +
**one** play mode entry, runs each job's driver back to back, and fans the screenshots/logs back to
each lane. Serialized execution, parallel authoring, and the expensive setup cost is amortized.

Cost model for 4 lanes each needing a runtime check:

| | naive (today) | Layer 1 only | Layer 1 + 2 |
| --- | --- | --- | --- |
| compiles | 4 (+ races) | 4 | **1** |
| play mode entries | 4 | 4 | **1** |
| wall clock | corrupt results | ~4 × 35 s = 140 s | ~35 s + 4 × 4 s = **51 s** |
| agent blocked | yes | yes, in queue | no — submit and keep working |

## 4. Layer 0 — Lane identity

A **Lane** is one agent's workspace: id, title, owned paths, verify scene, verify tier.

Two ways to bind an MCP session to a lane (implement both):

1. **Env var** — `MATRIX_LANE=ui`. `Feeder.StdioShim` reads it and sends `X-Matrix-Lane: ui` on the
   HTTP session it opens. Matrix Space panes spawn the CLI themselves, so they set it for free.
2. **Explicit tool** — `matrix-lane-attach { laneId, title?, owns? }` as the agent's first call,
   binding `laneId` to `McpClientTracker`'s `sessionId`. For hand-run terminals. Enforced by a line
   in each lane's `CLAUDE.md`.

Unattached sessions get an auto lane (`lane-<n>`) and a warning; they are treated as low priority
and denied writes outside a shared scratch folder.

### 4.1 Lane manifest — `MatrixSpace/lanes.json`

The Arbiter protects the *Editor*. The manifest protects the *files* — this is what stops two
agents overwriting each other's scripts, which no lock can prevent.

```json
{
  "lanes": [
    {
      "id": "ui",
      "title": "Shop UI",
      "owns": ["Assets/Game/UI/Shop/**", "Assets/_Verify/ui/**"],
      "verifyScene": "Assets/_Verify/ui/ui.unity",
      "verifyTier": "isolated"
    },
    {
      "id": "combat",
      "title": "Damage system",
      "owns": ["Assets/Game/Combat/**", "Assets/_Verify/combat/**"],
      "verifyScene": "Assets/_Verify/combat/combat.unity",
      "verifyTier": "runtime"
    }
  ],
  "shared": {
    "readable": ["Assets/**"],
    "writable": ["Assets/Shared/**"],
    "writePolicy": "warn"
  }
}
```

Enforcement point: a `LaneWriteGate` check at the top of every mutating tool
(`script-update-or-create`, `script-delete`, `rql-mutate`, `assets-move`, `assets-delete`,
`gameobject-*`, `scene-save`, `package-*`). Outside `owns` ∪ `shared.writable`:
`writePolicy: "warn"` logs and proceeds, `"reject"` throws with the owning lane's name. Start with
`warn`, flip to `reject` once the manifest is trustworthy.

`lanes.json` also generates each lane's `CLAUDE.md` preamble ("you are lane `ui`, you own …, verify
with `verify-submit`, never call `editor-application-set-state` directly").

## 5. Layer 1 — The Arbiter

`Feeder.MCP.Editor.Arbiter.EditorArbiter` — generalize `MatrixSpaceReloadCoordinator`. Single
instance in the Editor, backed by a `ScriptableSingleton` so leases survive domain reload.

### 5.1 Resource classes and lease modes

```csharp
enum EditorResource { None, Read, Write, Scene, Prefab, World }
```

| Mode | Grants | Blocks |
| --- | --- | --- |
| `Read` (shared) | reads, `screenshot-isolated`, `screenshot-camera`, `console-get-logs` | nothing |
| `Write` (exclusive vs `World`) | script/asset writes → compile → reload | `World` |
| `Scene` (exclusive) | `scene-open`, `scene-save`, prefab stage, `gameobject-*` in scene | `Scene`, `World` |
| `World` (exclusive vs all) | play mode, `tests-run`, `screenshot-game-view` while playing | everything |

Classification lives in `ToolResourceTable` — a `Dictionary<string, EditorResource>` keyed by the
tool-id constants already defined on each tool class (`EditorApplicationSetStateToolId`, …).
**Unlisted tool ⇒ `Write`** (conservative, matching ADR-0003's "absent metadata is treated as
mutating"). A unit test asserts every registered tool id appears in the table, so new tools cannot
silently slip through unclassified.

### 5.2 Lease semantics

```csharp
sealed record Lease(string LeaseId, string LaneId, EditorResource Resource,
                    DateTime ExpiresUtc, string Reason);
```

- Bound to **laneId**, not to a SignalR connection — survives domain reload the same way
  `_pendingOps` does (protocol §5).
- **TTL + heartbeat.** Default 120 s, renewed by any tool call from that lane. Expired lease is
  reclaimed with a `Debug.LogWarning` and the holder's next call fails with
  `LEASE_EXPIRED` — a crashed terminal cannot wedge the Editor.
- **Queue: FIFO with per-lane round-robin**, max one in-flight lease per lane, so one lane cannot
  flood. Interactive lanes (a human-driven pane) get priority over background lanes.
- **Waiting reports progress, not failure.** The `ToolProgress` message already exists in FMP: emit
  `"queued behind lane 'combat' (verify job vfy_7), position 2, ~18s"` every second. Only after
  `deadlineUnixMs` does it become a retriable `server busy` error (ADR-0003 §6).

### 5.3 The write gate

The nastiest bug class today: lane `ui` saves a `.cs` file while lane `combat` is in play mode.
Unity recompiles, domain-reloads, and `combat`'s run dies halfway with no explanation.

Rule: **while a `World` lease is held, writes are queued, not applied.** `script-update-or-create`
returns `{ queued: true, position: n, appliesAfter: "vfy_7" }` immediately instead of blocking for
30 s. The Arbiter flushes the queue as the first step of its next cycle — one compile for all
queued writes from all lanes.

Reuse `MatrixSpaceReloadCoordinator`'s `LockReloadAssemblies()` so compilation may *finish* while a
`World` lease is active but assemblies do not *load* until it is released.

### 5.4 Ownership checks on existing tools

- `editor-application-set-state` — requires a `World` lease held by the calling lane. Otherwise
  throw: `WORLD is held by lane 'combat' (job vfy_7, ~14s remaining). Use verify-submit instead of
  driving play mode directly.` This one change alone stops the worst corruption.
- `screenshot-game-view` — while playing, requires the `World` lease; otherwise you capture another
  lane's frame. Suggest `screenshot-camera` in the error.
- `tests-run` — requires `World`; the Arbiter is the normal caller.
- `scene-open` / `scene-save` / `assets-prefab-open` — require `Scene`.

## 6. Layer 2 — Verify jobs

### 6.1 Pick the cheapest verification tier

Most "check my work" needs are not runtime needs. Routing them away from `World` removes most
contention before any queueing happens:

| Tier | Tools | Lease | Parallel? |
| --- | --- | --- | --- |
| `isolated` | `screenshot-isolated`, `screenshot-camera`, `ui-inspect-*` | `Read` | effectively yes |
| `editor` | `screenshot-scene-view`, scene inspection | `Scene` | serialized, cheap (~1 s) |
| `runtime` | play mode behaviour, physics, input, coroutines | `World` via job queue | batched |

Lane `ui` assembling a Shop panel lives almost entirely in `isolated` — render the prefab
off-screen, inspect the UI Toolkit tree, no play mode at all. Push agents there via `CLAUDE.md` and
via `verifyTier` in the manifest.

### 6.2 Job submission

```
verify-submit {
  laneId?,                      // inferred from session
  driver: "Game.Combat.Verify.DamageScenario",   // preferred
  steps?: [ ... ],              // DSL, for trivial cases
  scene?: "Assets/_Verify/combat/combat.unity",
  timeoutSec?: 60,
  priority?: "normal" | "interactive"
}  →  { jobId: "vfy_7", position: 2, etaSec: 24 }   // returns immediately

verify-await  { jobId, timeoutSec }  →  { status, captures[], logs[], assertions[], durationMs }
verify-cancel { jobId }
verify-status { laneId? }  →  queue + in-flight + last results
```

**Driver-first, DSL second.** A step DSL will always be missing the op you need; agents write C#
well. A driver is a plain class in the lane's own folder:

```csharp
public sealed class DamageScenario : IVerifyScenario
{
    public IEnumerator Run(IVerifyContext ctx)
    {
        yield return ctx.LoadScene("Assets/_Verify/combat/combat.unity");
        var enemy = ctx.Find<Enemy>("Enemy");
        yield return ctx.Shot("before");          // PNG → Library/matrix/verify/vfy_7/before.png
        enemy.TakeDamage(50);
        yield return ctx.WaitFrames(2);
        yield return ctx.Shot("after");
        ctx.Assert(enemy.Hp == 50, "hp should be halved");
    }
}
```

`ctx.Shot()` uses `Camera.Render()` into a `RenderTexture` — **not** the Game View window — so it
works when Unity is unfocused and captures the right lane's frame by construction.

For the trivial "just show me the screen" case, a small DSL avoids a round-trip through compilation:
`[{"op":"loadScene",…},{"op":"wait","seconds":0.5},{"op":"capture","name":"boot"}]` with ops
`loadScene`, `wait`, `waitFrames`, `waitUntil`, `invoke`, `capture`, `assert`. `waitUntil`/`invoke`
expressions go through the Roslyn pipeline already used by `script-execute`.

### 6.3 The batch cycle

`EditorArbiter` ticks on `EditorApplication.update`:

```
if (playing && currentJob != null)      → pump current job, enforce per-job timeout
else if (idle)
    1. writeQueue non-empty?            → flush all queued writes, AssetDatabase.Refresh(),
                                          release the reload lock, wait for compile+reload, return
    2. compilation errors?              → attribute to the owning lane(s) (see §8.1),
                                          fail their queued jobs fast, tell other lanes
                                          "blocked by lane X", return
    3. jobQueue non-empty?              → acquire World
                                          enter play mode ONCE
                                          foreach job (round-robin across lanes):
                                              begin log capture window for this job
                                              run driver in try/catch with timeout
                                              collect captures + logs + assertions
                                              reset: unload additive scenes, restore statics
                                          exit play mode
                                          release World, publish results
```

A hanging or throwing job is isolated by its own timeout and `try/catch`; it is quarantined and the
batch continues with the next job. Job state lives in a `ScriptableSingleton` (same pattern as
`MatrixSpaceSessionStore`), so an unexpected domain reload resumes the batch instead of losing it.

### 6.4 Per-lane console attribution

During each job's window, subscribe `Application.logMessageReceived` into that job's own bucket.
`verify-await` returns only that lane's logs. This is cheap and removes a whole category of agents
debugging each other's exceptions. Do the same for Editor-side work by tagging
`console-get-logs` results with the lease holder at each log's timestamp.

## 7. Layer 3 — Editor pool (opt-in, real parallelism)

When two lanes both need long runtime sessions, serialization becomes the bottleneck. Then, and
only then, run more than one Editor over the **same** `Assets/`.

### 7.1 Clone mechanics (Windows)

ParrelSync's approach: a sibling folder with directory **junctions** back to the original.
Junctions (`mklink /J`) need no admin rights, unlike symlinks.

```
D:\Unity\TheMatrix\            (primary — owns all writes)
D:\Unity\TheMatrix_run1\       Assets\ Packages\ ProjectSettings\  → junctions
                               Library\ Temp\ obj\                 → its own (real folders)
```

`matrix-editor-clone { name }` creates the junctions and launches
`Unity.exe -projectPath D:\Unity\TheMatrix_run1`. First import of a clone is slow (its `Library` is
cold) and costs several GB of disk plus a few GB of RAM — budget for it.

### 7.2 Required server changes

- `UnityLinkRegistry`: key by `(projectId, unityInstanceId)`; keep a per-project index. Drop
  newest-wins for the multi-instance case (ADR-0003 already anticipates "a future flag may allow
  explicit multi-instance addressing").
- `UnityRegisterRequest`: add `[Key(11)] Role` — `primary` | `runner`.
- `BackendRouter.Resolve()`: route by the calling session's lane → its assigned Editor; fall back
  to `primary`. Cross-project isolation stays as ADR-0003 specifies.
- `matrix-lane-attach { editor: "primary" | "run1" }` binds the affinity.

### 7.3 Write discipline (non-negotiable)

Both Editors import the same physical `Assets/`, and each writes `.meta` files. Concurrent imports
race.

- **Runners are read + run only.** All write tools are rejected server-side for `runner`-role
  Editors.
- **Auto Refresh off in runners.** The Arbiter calls `assets-refresh` on a runner only *after* the
  primary's compile has completed — never mid-write.
- `ProjectSettings` is junctioned, so Play Mode Options and Player Settings stay consistent across
  the pool. That is desirable.

### 7.4 Variant 3b — batch-mode runner

For unattended regression while the human keeps authoring, skip the interactive clone:

```bash
Unity.exe -projectPath D:\Unity\TheMatrix_run1 -batchmode -runTests \
  -testPlatform PlayMode -testFilter Game.Combat.Verify -testResults out.xml
```

Zero interference with the primary Editor, at the cost of a ~30–60 s Unity boot per batch. Good for
"verify everything before I commit"; too slow for the iterative loop.

### 7.5 Recommendation

Layers 1 + 2 deliver most of the value at a fraction of the cost. Add **one** runner Editor
(primary + `run1`) only when the verify queue is measurably the bottleneck. Beyond two Editors, the
disk, RAM, and double-import cost usually exceeds the throughput gained.

## 8. Editor configuration prerequisites

These settings are what make a serialized queue *feel* parallel. Without them the design works but
is slow.

1. **Player Settings → Run In Background: ON.** Critical. Without it, play mode stalls whenever
   Unity loses focus — and the agent's terminal has focus, not Unity. MCP-driven verification would
   hang.
2. **Project Settings → Editor → Enter Play Mode Options: enabled, Reload Domain OFF, Reload Scene
   OFF.** Play mode entry drops from ~10–20 s to well under a second, which is the single highest
   leverage change for queue throughput. **Trade-off:** statics no longer reset between entries, so
   jobs can contaminate each other. Mitigate in `IVerifyContext` — the runner resets registered
   static state between jobs, and drivers declare what they touch. If a feature genuinely depends
   on domain-reload semantics, mark its job `requiresDomainReload: true` and the Arbiter gives it
   its own play mode entry.
3. **Prefer `screenshot-camera` over `screenshot-game-view`.** Explicit `RenderTexture` render:
   focus-independent, size-deterministic, no dependency on a Game View window existing or being
   unobscured.
4. **A Game View must still exist and be reasonably sized** for any tool that reads it, and its
   resolution should be pinned (fixed 1920×1080, not "Free Aspect") so screenshots are comparable
   across jobs and lanes.

## 9. Failure modes

| Failure | Handling |
| --- | --- |
| Lane A's compile error blocks everyone | Attribute the failing assembly to the lane owning those paths (`lanes.json` globs vs `CompilerMessage.file`). Tell A the errors; tell others `BLOCKED_BY_LANE: ui`. Never let a lane silently wait on someone else's mistake. |
| Terminal crashes holding `World` | Lease TTL + heartbeat reclaims it; warning logged; a `FORCE RELEASE` button in the Matrix Space window for the human. |
| Domain reload mid-batch | Jobs and leases persisted in a `ScriptableSingleton`; resumed on `OnAfterAssemblyReload`. `_pendingOps` already survives by `operationId`. |
| Job hangs | Per-job timeout inside the batch; job quarantined, batch continues, lane told `TIMEOUT`. |
| Two lanes edit the same file | `LaneWriteGate` + `lanes.json`. Locks cannot help here. |
| Lane starvation | Round-robin across lanes, max one in-flight lease per lane, priority for interactive lanes, FIFO aging. |
| Play mode exception storm | Per-job log capture window bounds what each lane sees; cap bucket size. |

## 10. UX in the Matrix Space window

- **World status bar:** current holder (lane colour + job id), queue depth, ETA, compile state.
- **Per-pane badge:** `RUNNING` / `QUEUED (2)` / `BLOCKED (compile: ui)` / `WRITES QUEUED (3)`.
- **Verify gallery:** latest captures per lane side by side — the human sees all features at a
  glance without opening any terminal.
- **FORCE RELEASE** for a wedged lease, and **PAUSE ARBITER** so the human can take the Editor back
  for manual work (all lanes queue until resumed). This matters: the human must always be able to
  reclaim their own Editor.

## 11. New/changed MCP tool surface

| Tool | Status |
| --- | --- |
| `matrix-lane-attach`, `matrix-lane-list` | new |
| `matrix-world-acquire`, `matrix-world-release`, `matrix-world-status` | new (escape hatch for agents that truly need raw play mode) |
| `verify-submit`, `verify-await`, `verify-cancel`, `verify-status` | new |
| `matrix-editor-clone` | new (Layer 3) |
| `editor-application-set-state` | + lease ownership check |
| `screenshot-game-view` | + lease check while playing |
| `tests-run` | + `World` lease required |
| `scene-open`, `scene-save`, `assets-prefab-open` | + `Scene` lease required |
| `script-update-or-create`, `script-delete`, `assets-*`, `gameobject-*`, `package-*` | + write gate + `LaneWriteGate` |

## 12. Phased rollout

**Phase 1 — Stop the corruption** (~1–2 days)
Lane identity (env var + `matrix-lane-attach`) · `EditorArbiter` with `Read`/`Write`/`Scene`/`World`
leases · `ToolResourceTable` + coverage test · ownership check on `editor-application-set-state` and
`screenshot-game-view` · write gate · queue progress via `ToolProgress` · Editor config from §8.
→ Two terminals can already work all day without destroying each other's runs.

**Phase 2 — Throughput** (~3–4 days)
`verify-submit`/`verify-await` · `IVerifyScenario` + `IVerifyContext` + `ctx.Shot()` ·
batch cycle · per-job log attribution · `lanes.json` + `LaneWriteGate` · per-lane verify scenes ·
Matrix Space world status bar + verify gallery.
→ Agents stop blocking; N lanes cost roughly one lane's verification time.

**Phase 3 — Real parallelism** (~2–3 days, only if measured as needed)
Registry keyed by `unityInstanceId` · `Role` in the register request · lane→Editor routing in
`BackendRouter` · `matrix-editor-clone` · runner write rejection + gated `assets-refresh`.

## 13. Open questions

1. Should `verify-submit` compile-and-run in one shot (submit driver source inline), or require the
   driver to already exist? Inline is fewer round-trips but makes compile attribution murkier.
2. Should `Write` leases be per-assembly rather than global? Two lanes in different asmdefs could
   compile independently in principle — but Unity's compile pipeline is global, so probably not
   worth it.
3. How much static-state reset can `IVerifyContext` do generically with domain reload off, before
   drivers must declare their own reset? Start with drivers declaring, measure the pain.
4. Does the human's own manual Editor work need a formal lane, or is `PAUSE ARBITER` enough?
