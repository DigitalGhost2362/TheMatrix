# HyperTesting (Matrix Bridge, experimental)

This folder belongs to **Hyper Testing Mode (Experimental)**: several clone Unity Editors play test this
project in parallel, each driven by its own `hyper-playtester-N` subagent.

Turn it on or off only from **Tools > Feeder > Hyper Testing (Experimental)**. Agents cannot enable it.
The switch is per machine and per project, and every Matrix Bridge package update turns it off again.

| Path | What it holds |
|---|---|
| `config.json` | Clone cap, memory reserve and per-clone estimate, import workers, GPU limit, ports, clone folder, light-editor options |
| `Journal/playbook.md` | The best known play-test flow. Read before every session, update after |
| `Journal/clone-issues.md` | Problems creating, launching, refreshing or stopping clones (`[auto]` lines come from the pool) |
| `Journal/playtest-issues.md` | Problems while play testing: tools, focus, timeouts, flaky steps |
| `Journal/ram-observations.md` | RAM peaks and low-RAM alerts (`[auto]` lines come from the sampler) |
| `Journal/ram-samples.csv` | Memory sampled every 30 s while clones run: system commit, and RAM, commit and import workers of the original and of each clone (ignored by git; an older column layout is moved aside to `ram-samples-<time>.old.csv`) |
| `Journal/matrix-improvements.md` | Concrete changes Matrix MCP should make, distilled from the journal |
| `Runs/<run id>/` | Per-run results, screenshots and `SUMMARY.md` (ignored by git) |
| `tools/` | Scripts that drive a clone over HTTP when its MCP tools are not loaded: `hc.py`, `hx.sh`, `hxf.sh`, `shot.py` (rewritten on every `up`) |

Clones live next to this project in `<project>.HyperClones/hc<N>` unless `config.json` says otherwise.
Delete them with the window's **Delete clones** button, never with a recursive delete: `Assets` inside a
clone is a junction to this project's `Assets`.

## config.json

| Key | Default | Meaning |
|---|---|---|
| `maxClones` | 3 | Upper bound for `up` |
| `clonesRoot` | empty | Folder for clones; empty = `<project>.HyperClones` next to the project |
| `basePort` | 27100 | Clone N gets `basePort + N` (or the next free port, step 10) |
| `ramReserveMb` | 4096 | Memory that must stay free: `up` refuses when the commit headroom (commit limit minus commit charge) left after one more clone would be below it, and the sampler alerts when free RAM or free commit is below it |
| `ramPerCloneEstimateMb` | 4096 | Lowest commit one more clone is budgeted at; the measured clone peak x1.2 and the original editor's current commit win when larger |
| `maxParallelCompiles` | 1 | Clones that may refresh/compile at the same time |
| `compileTimeoutSec` | 300 | How long `refresh` waits for one clone |
| `jobWorkerCount` | 2 | `-job-worker-count` for clone editors (0 = Unity default) |
| `belowNormalPriority` | true | Clone editors run at BelowNormal CPU priority |
| `playTargetFrameRate` | 30 | Frame cap in clone play mode (0 = no cap) |
| `playLowestQuality` | true | Quality level 0 in clone play mode, restored on exit |
| `isolatePlayerPrefs` | true | Clone product name gets `_hcN` so PlayerPrefs / save paths do not collide |
| `closeSceneViews` | true | Close Scene views in clones (each one renders a second camera) |
| `ramSampleIntervalSec` | 30 | Memory sampler period |
| `cloneImportWorkers` | 0 | Asset import workers a clone may run (0 = the clone imports in its own editor process; each warm worker holds 1.5-2 GB of commit) |
| `vramOneCloneMaxMb` | 4096 | A GPU with this much video memory or less carries one clone at a time (0 = no limit) |

Changes apply to clones on their next `up` (the marker file is rewritten then).

A clone never writes the Asset Pipeline preferences: its import worker count is set in memory, and its search
index is reduced in its own `UserSettings` (no types or properties, and no indexing at editor start on Unity 6).
A clone that creates an asset creates it in this project, so the clone reports every such path as CRITICAL. `up`
also takes a `git status` baseline of this project, and `down` and `status` warn about untracked paths that
appeared since and that no clone reported (files you add yourself show up there too).
Packages that write assets under `Assets/` when play starts do not work with clones.
