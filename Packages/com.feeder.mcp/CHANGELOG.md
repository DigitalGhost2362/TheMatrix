# Changelog

All notable changes to this package are documented here.

## [Unreleased]

### Changed

- Install from the Git URL only, and update with **Update** in Package Manager. The Matrix registry and
  the installer `.unitypackage` are no longer published. Projects on the registry replace the version
  number in `Packages/manifest.json` with the Git URL and remove the `Matrix` scoped registry.

## [1.0.1] - 2026-10-21

### Changed

- Skill files are regenerated only when the package version or the set of enabled
  tools changed, instead of on every Editor load.
- Matrix Setup regenerates the skills as one of its steps.
- `matrix-shared-editor` holds `Temp/agent-editor-lock` for one burst of Editor work (a compile,
  one play session, one scene edit) instead of the whole task:
  - release before any step that does not need the Editor, and after a failure;
  - 15-minute cap per claim, heartbeats only inside a burst;
  - retry a busy lock for up to 10 minutes before writing a test-case handoff;
  - the verifier claims the lock per test case and releases it while writing a fix.
  The lock scripts, `lock.json` and the 20-minute TTL are unchanged, so old and new sessions share
  one lock.

## [1.0.0] - 2026-10-19

### Added

- **Query tools** - read and change many objects with one pipeline query:
  `source | stage | stage`.
  - `rql-query`:
    - **Sources:** `objects`, `roots`, `selection`, `scenes`, `logs`, `components`, `prefab`,
      `assets("t:Material"[, folder])`, `asset(path)`, `find(path or name or id)`, `obj(id)`, or any expression.
    - **Stages:** `where`, `select ... as`, `sort ... desc`, `take`, `skip`, `first`, `last`, `count`, `reverse`,
      `distinct`, `group`, `sum`/`avg`/`min`/`max`, `get`, `components`, `children`, `descendants`, `parent`,
      `gameobject`.
    - **Expressions:**
      - Members can reach into structs and components, for example `transform.position.x` or
        `Light.intensity` on a GameObject.
      - Shader properties are read by name, for example `material["_BaseColor"]`.
      - Meta fields: `$path`, `$id`, `$type`, `$index`.
      - Operators: comparison, `contains`, `startswith`, `endswith` (all three ignore case), `matches`
        (regex), `in`, `is Type`, `??`, arithmetic, and vector and color operators.
      - Functions such as `vec`, `color`, `euler`, `has`, `comp`, `distance`, `round` and `iif`.
    - **Output:** a compact text table by default, or JSON with `format=json`. `limit` caps the rows; the
      total count is always reported.
    - Reading a missing member returns `null` and adds a warning.
    - Only reading methods can be called.
  - `rql-mutate`:
    - **Stages:** `set PATH = EXPR, ...` writes through structs, components, shader properties and array
      elements, and converts values to the member's type. `call`, `add Type` and `destroy` are also
      available, and `new("Name"[, primitive][, parent])` creates objects.
    - **All-or-nothing:**
      - Every target is checked first, and an error lists each problem.
      - If a later stage fails, the earlier stages are rolled back.
    - The whole query is one undo step, `Matrix query`.
    - `dryRun` reports each change as `target | action | old | new` and reverts it.
    - `maxTargets` (default 100) refuses wider changes.
    - `save` saves the touched scenes, prefab and assets. Without it they are only marked dirty.
  - `rql-schema` shows the syntax card, every function and the component types in the open scenes, with
    counts. With `typeName`, it lists that type's members (read only or read-write), its methods and its
    enum values.
  - From C#: `Feeder.MCP.Editor.Query.MatrixQuery.Run` and `MatrixQuery.Mutate`.
- Setup now keeps `rql-query` and `rql-schema` in the core tool set. The Skills window has a new
  **Query** group.

### Changed

- `script-execute` in body-only mode:
  - A body that returns a value, such as `return go.name;`, compiles as a method returning `object`, and
    the value comes back as the result.
  - A body with no value return still compiles as `void`.
  - A `return x;` inside a lambda or a local function does not count as a value return.
  - A body that returns a value on some paths but not others fails with an error that explains the fix.
- VFX Studio is marked experimental: the menu is now **Tools > Feeder > VFX Studio (Experimental)**.
- The `matrix-vfx` skill file is written only while at least one `vfx-*` tool is enabled, and is removed
  when they are all off. Projects that do not use VFX no longer carry it.
- A VFX Studio tab restored from the Editor layout stays idle until it is first shown: the effect
  library scan no longer runs on every Editor start and domain reload behind a hidden tab.

### Removed

- The tools that the query tools replace. Use these queries instead:
  - `gameobject-find`, `gameobject-component-get`, `object-get-data`, `scene-get-data`, `scene-list-opened`,
    `assets-find`, `assets-get-data`, `editor-selection-get` -> `rql-query` with `find(...)`, `obj(id)`,
    `roots`, `scenes`, `assets("filter")`, `asset(path)`, `selection`, then `select`, `get` or
    `components`.
  - `gameobject-create`, `gameobject-destroy`, `gameobject-modify`, `gameobject-set-parent`,
    `gameobject-component-add`, `gameobject-component-destroy`, `gameobject-component-modify`,
    `object-modify`, `assets-modify` -> `rql-mutate` with `new(...)`, `destroy`, `set`,
    `set parent = ...`, `add Type`, `components Type | destroy`, and `save=true` for assets.
  - Setup's core tool set drops them.
  - Skill generation now also deletes the `matrix-<tool>` skills it wrote for tools that no longer exist.
    Skills that other tools or people wrote are left alone.
  - Clients and saved prompts that call these names get "tool not found". Switch them to the queries.

### Fixed

- `ComponentDataShallow.isEnabled` reports whether a Collider or Renderer is enabled. Before, it was
  `NA` for them.
- JSON input accepts combined values of `[Flags]` enums, for example `"FreezePositionX, FreezeRotation"` or
  a number. Before, they were rejected.
- Serializing an object graph with a cycle writes correct `$ref` paths after the first back-reference.
  Before, every later path carried an extra segment and could not be resolved.
- Writing an object into a field that is `null` through a path (`TryModifyAt`) works.
- Log entries still buffered when the editor closes or reloads are written to the log file instead of
  being lost.

## [0.89.0] - 2026-10-16

### Added

- **Matrix VFX** - ParticleSystem effects built and tuned by an agent, still fully editable by people.
  - **Tools** (off by default, turn them on with `tool-set-enabled-state`):
    - `vfx-apply` creates or patches an effect prefab from a compact JSON spec. Emitters are addressed by
      name and only changed fields are sent. Module and property names are the Unity scripting API names.
      `root.scale` sets the scale of the prefab root.
    - `vfx-read` returns the prefab as the same spec, listing only values that differ from the baseline
      emitter, plus `root.scale` when the prefab root is scaled.
    - `vfx-preview` simulates with a fixed seed and renders one contact sheet of time samples, each
      labelled, from a fixed camera. It also returns text stats: alive counts, per-emitter windows, fill
      and warnings. `context=<model prefab>` renders the effect on the model it decorates.
      `camera=<scene camera>` renders the instance placed in the open scene through that camera, including
      bloom and tonemapping.
    - `vfx-texture` generates procedural particle textures: glow, circle, ring, spark, star, sparkle (pointed
      4-point twinkle), flare, crescent, smoke flipbook, tiling noise, trail, shockwave, polygon and
      lightning.
    - `vfx-history` lists, scores and restores versions. Every apply stores one, and manual Inspector edits
      are captured before the next apply.
  - **Materials:** a `"texture/blend"` shorthand (`add`, `alpha`, `premul`, `mul`) creates one shared
    material per pair. It uses URP Particles/Unlit when URP is active and Particles/Standard Unlit
    otherwise. A shared material whose shader no longer matches the active pipeline is switched on its next
    use. Flipbook textures switch on the Texture Sheet Animation module automatically.
  - **VFX Studio window** (**Tools > Feeder > VFX Studio**), Effect tab:
    - **Library:** every prefab under `Assets/` with a ParticleSystem on the root or on a child, in a folder
      tree that lists only folders holding effects (with counts) and a grid of thumbnails with the emitter
      count. Search by name; double-click a folder to enter it, double-click an effect or press **Open** to
      edit it. It scans when the window opens and on **Refresh**, reloading only the prefabs that changed.
    - **Editor:** the prefab's real hierarchy, with a checkbox per emitter that hides it in the previews
      only. Right-click a child > **Copy Path** copies `Assets/VFX/Hit.prefab > Debris/Pop`, the exact
      object to name when asking for a change. A live preview of the whole effect (play, pause, restart,
      scrub, 0.25x, 0.5x, 1x) loops over the longer of the root Duration and the effect's own length. The
      root shows Duration, Simulation Speed and Scale (saved as `root.scale`); an emitter shows its own
      preview and its main, emission, shape, over-lifetime and renderer values.
    - Edits stay on an in-memory copy, and changed values get a blue bar. **Save** writes only those values
      through the `vfx-apply` path and stores a version, **Undo** drops them, and leaving with unsaved
      edits asks first. Ranges get a two-handle slider; curves and gradients are shown but edited in the
      Inspector or with `vfx-apply`. A row that does not apply (module off, emitter driven by rate) is
      greyed with the reason. Save is blocked while two emitters share a name. Unsaved edits are lost when
      scripts recompile.
    - **Textures** tab: the texture generator.
  - **Skill:** `matrix-vfx` covers the iteration loop, the spec reference, layering recipes per effect
    type, a symptom-to-cause table and mobile budgets.

## [0.88.0] - 2026-10-15

### Added

- The Matrix server answers a `Compile` call over FMP/1 (C# source and reference paths in, assembly bytes or
  diagnostics out) and lists `compile` in the capabilities of its register reply.
- **Hyper Testing Mode (Experimental)** (**Tools > Feeder > Hyper Testing (Experimental)**, Windows only):
  several clone editors play test one project in parallel, each driven by its own `hyper-playtester-N`
  subagent.
  - **Clones:** each clone links `Assets` and the embedded packages back to the project through directory
    junctions, gets its own `Library` copy and its own server port, and runs as a light, read-only runner
    (auto refresh off, in-process import, asset writes blocked, frame cap and lowest quality in play mode,
    PlayerPrefs isolated per clone).
  - **Switch:** it is off by default and stays off for anyone who updates the package. Only a person can
    turn it on, by ticking a box and typing the project folder name in the window. The switch is per
    machine and per project, and turns off again on every package update. `script-execute` and
    `reflection-method-call` refuse code that touches it.
  - **Tool and skill:** the new `hyper-testing` tool (status, up, refresh, down, purge, ram) runs the clone
    pool behind a free-RAM gate. The `matrix-hyper-testing` skill guides the orchestrating agent.
  - **`HyperTesting/` folder:** a config file and a journal (clone issues, play-test issues, RAM
    observations with a 30 s sampler, playbook, Matrix improvements) where agents record what went wrong
    and what worked.
  - **Memory gate:** `up` counts commit, the limit Windows actually runs out of. It refuses when the commit
    limit minus the commit charge cannot hold one more editor plus `ramReserveMb`, budgeting a clone at the
    larger of the measured clone peak x 1.2 and the original editor's commit, and names the fix (a larger
    fixed pagefile, a lighter scene, a restart of the original). It warns when Gradle or another Unity is
    running, and starts only one clone on a GPU with 4096 MB of video memory or less (`vramOneCloneMaxMb`).
  - **Import workers:** a clone imports in its own editor process. `cloneImportWorkers` in `config.json`
    (default 0) sets how many asset import workers it may run. The count is set in memory only; the
    machine-wide import preferences are never written.
  - **Smaller clones:** a new clone does not copy `Library/Search`, its search index keeps no types,
    properties or dependencies (and on Unity 6 it does not index at editor start), and it opens no scene on start.
  - **Sampler:** `ram-samples.csv` has the system commit charge and limit, and the RAM, commit and import
    workers of the original and of each clone. A file with the old columns is kept as
    `ram-samples-<time>.old.csv`. `status` shows the same numbers and warns when the original is in play mode
    while a clone imports or plays.
  - **Writes into the project:** a clone reports every asset it creates as CRITICAL, with its path. `up`
    also takes a git baseline of the project, and `down` and `status` warn about new untracked paths that no
    clone reported.
  - **Fallback tools:** `HyperTesting/tools/` holds `hc.py`, `hx.sh`, `hxf.sh` and `shot.py` to drive a
    clone over HTTP. `up` warns when the play tester files are newer than the Claude Code session.
  - The `matrix-hyper-testing` skill and the playbook start with a memory pre-flight.
- Skill `matrix-shared-editor`: the protocol for several agents and people on one Editor - an
  `agent-editor-lock` claim before compiling or playing, and a written test-case handoff when the
  Editor is busy. It is a convention between sessions; Matrix does not enforce it yet.
- **Tools > Feeder > Skills** (also in the Matrix Bridge `...` menu): a window with one switch per MCP
  tool, grouped by area, with a search box, an On/Off filter, the tokens each tool adds to every request
  and a meter for the total. A tool that works with another says so when that one is off. **Core only**
  and **Enable all** ask first, since enabling everything costs about 50k tokens per request. Switching a
  tool rewrites the skill files after a second; reconnect the AI client afterwards.
- The Matrix Bridge tab icon dims while the editor is not linked.
- `editor-health`, a tool the server answers itself, even while the editor is hung, reloading or closed:
  whether the editor is responsive, busy, reloading, stalled or not linked, and what holds it (open editor
  windows and dialogs, Matrix calls in flight, last lines of the editor log).
- `tool-call`, a server tool that calls a just-enabled Unity tool before the client lists it. It refuses
  tools the session already lists, and tools that Claude Code deny or ask rules or PreToolUse hooks cover;
  it refuses everything while a settings file cannot be read. `enableToolCallProxy: false` turns it off.
- `script-execute` takes `timeoutSeconds` for code that runs long on purpose.
- `console-get-logs` takes `logTypes`, `sinceUtc`, `include` and `exclude` (regular expressions on the
  message) and `groupDuplicates` (one entry per identical message, with `Count`). Without them it returns
  the same entries as before.

### Changed

- **The package ships no `.dll`.** The 39 precompiled libraries are gone. The parts Matrix used are source
  in the package (`Feeder.Json`, `Feeder.Logging`, `Feeder.Rx`, `Feeder.Fmp`). Unity talks to the server
  through a small WebSocket client of the existing `/fmp` hub. JSON now writes floating-point numbers in the shortest form that reads
  back exactly (`0.1`, not `0.100000001`).
- **`script-execute` compiles on the Matrix server** (server 0.3.0 carries the compiler; the payload grows by
  10 MB) instead of inside the editor. The language version stays C# 12, the editor no longer freezes while
  code compiles, and the tool needs the server to be running. A server older than 0.3.0 answers that it
  cannot compile.
- **Every assembly is Editor-only.** `Runtime/link.xml` and the build-time link merging are removed.
- **Matrix keeps to names of its own.** The data types moved to the `Feeder.MCP.Data` namespace (custom
  tools must update their `using`). Skill folders are
  `matrix-<tool>`; folders that older versions wrote without the prefix are removed on the next load when
  their content is Matrix's. Environment variables start with `FEEDER_MATRIX_` and the old names are not
  read. Logs are in `Temp/feeder-matrix`, and the play-mode object is `Feeder.MainThreadDispatcher`.
  The core assemblies and namespaces are now `Feeder.ToolHost`, `Feeder.ToolHost.Common` and
  `Feeder.Reflect`, and the plugin classes are `MatrixPlugin`, `MatrixPluginEditor` and
  `MatrixPluginRuntime`; custom tools update their `using` lines (for example
  `Feeder.Reflect.Utils.MainThread.Instance.Run(...)`).
- The **Tools > Feeder** menu is shorter: Setup..., Skills, Bridge, Space (experimental) and Server (the
  old MCP submenu, now with Reset Config and Debug inside it). Until Matrix is turned on, only Setup...
  works and the other items are greyed out. The windows are still called Matrix Bridge and Matrix Space.
- `unity-tool-list` now reports whether each tool is enabled, and its description says that only a core set is on by default.
- Skill files are written only for enabled tools, and the folders of disabled tools are removed, so the
  lean tool set no longer ships 70 skills for tools the agent cannot call. Turn a tool on with
  `tool-set-enabled-state`, then run `unity-skill-generate`; skills also refresh on every editor load.
  A project that tracks `.claude/skills` or `.agents/skills` in git sees the disabled tools' skill folders
  deleted on the next refresh.
- Matrix Space agents are told to call `unity-tool-list` and `tool-set-enabled-state` when a Unity tool
  is missing from their list.
- Matrix Setup also gitignores `.codex/config.toml`, which holds this machine's port.
- The local server executable is now `matrix-server.exe`, installed in `Library/feeder-matrix`. On
  update, the project's old Matrix server is stopped and the files it left in `Library` are removed, only
  when every file in that folder is Matrix's own, and wired agents get their config rewritten in the same
  editor session. Restart the agent session afterwards. Nothing is touched while Matrix is off.
- The default tool timeout is now 60 s (was 10 s), so heavy editor work such as builds and NavMesh bakes
  is no longer cut off mid-run. Projects still on the old stored default of 10 s move to 60 s on the next
  editor load; any other value is kept.
- When another project's server, or any other process, already listens on this project's port, the
  bridge moves to a free port in 20000-29999, rewrites the config of every wired agent, and says so on the
  Bridge window. Restart the agent session to pick up the new port.
- The server tells MCP clients when the tool set changes (`tools/list_changed`), so a tool switched on or
  off mid-session shows up or disappears without reconnecting.
- A tool call waits for an editor that is reloading (`reconnectGraceMs`, default 180 s) instead of
  failing with "No Unity editor is connected", and the time the link is down does not count against its
  deadline. Tools that finish after a compile or domain reload get `deferredToolTimeoutMs` (default
  15 minutes). Every timeout ends with the editor health summary.
- `editor-application-set-state` answers once play mode is entered or left, domain reload included.
- The editor sends a heartbeat from a background thread; the server version is 0.4.0.
- A tool failure caused by the caller (bad input, or code passed to `script-execute` that does not compile
  or throws) logs one `[Matrix tool]` warning line instead of a red error with a stack trace; the caller
  still gets the whole message. Tools throw `ToolCallerException` for this. Faults of the tools themselves
  stay errors.
- `script-execute` reports the exception type, its message and only the frames of the caller's script.
- `script-execute` keeps the last 32 compiled images by source and reference set, so running the same
  code again skips the compile. Each run still loads a fresh copy, so static fields start empty as before.
  The time of each stage is logged at Debug level, and the skill body shows how to keep data between calls
  and how to sample something every frame from inside Unity.
- The `matrix-shared-editor` skill has ready lock scripts (acquire, heartbeat, release, stale break) for
  POSIX shell and PowerShell, a rule for a stuck Editor (read `editor-health` first, do not guess), and a
  rule for long jobs (`timeoutSeconds`, or a job started from `EditorApplication.update` with a marker file
  under `Temp/` that is read before anything is scheduled again).

### Fixed

- Compiling the package no longer floods the Console with about 830 CS8632 nullable-annotation warnings
  from `Feeder.Reflect`, `Feeder.ToolHost` and `Feeder.ToolHost.Common`.
- Writing an agent config file (`.mcp.json`, `.cursor/mcp.json`, `.codex/config.toml`,
  `.gemini/settings.json`) now replaces it atomically, keeps every entry that belongs to other tools, and
  leaves a file it cannot parse untouched instead of overwriting it.
- Several editors open at once no longer stop, kill or take over each other's MCP server. A server now
  belongs to the project whose `Library/feeder-matrix` it runs from. Its process id is kept in
  `Temp/feeder-matrix/server-owner.json` instead of the machine-wide editor preferences, and nothing is stopped
  unless it is confirmed to be this project's own server. This also covers hyper testing clones and
  projects still on an older version of the package.
- Unity registers only with its own project's server, never with whatever answers on the port.
- If the server dies during a session it is started again, after 1, 2, 5, 10 and 30 s. After five crashes
  within five minutes the Bridge shows FAULT with the reason; click it to retry.
- The Bridge no longer shows LINKING forever after the connection drops. It reconnects with the same
  back-off, shows what it is waiting for, and turns to FAULT when it gives up.
- Hyper testing `down` and `purge` no longer fail when run from an agent tool call.
- Hyper testing clones no longer inherit an MCP environment variable from the editor that started them,
  so a clone never connects to the original editor's server.
- Matrix Setup tells you to run `git rm --cached` only for a tracked config file that holds nothing but
  Matrix's entry. For a file that may hold other tools' entries it tells you to keep it tracked and run
  `git update-index --skip-worktree`.
- The MCP server now counts only agents active in the last 10 minutes, so sessions left behind by closed
  clients or one-off scripts no longer pile up in the Bridge count. Requires the next server release.

- Matrix Bridge: clicking `claude` or `deepseek` no longer switches both on and off. DeepSeek used to
  share Claude Code's `.mcp.json` entry, which DeepSeek Harness does not read. Its click now only keeps
  the skills in `.agents/skills` current, and its on/off state is its own. Add the bridge to DeepSeek
  Harness through its own MCP configuration.
- Asset import workers no longer run the bridge, the server manager, the Hyper Testing runner or any other
  editor startup code of this package. Every worker used to run all of it, including connecting to the server
  and adopting the project's server process.
- Hyper testing no longer reads 0 MB for every editor: the memory properties of the process API return 0
  inside Unity, so memory, liveness, child processes and command lines now come from Windows directly.
- `up` no longer reports "process exited" for a clone that is still starting, and `down` no longer has to
  kill a clone that was asked to quit: the quit runs from the update loop, which keeps running while the
  editor is in the background.
- A call cut off by a domain reload fails at once with a clear message instead of waiting for its
  deadline.
- Without a linked editor, `tools/list` returns the server's own tools instead of an error.
- `console-get-logs` returns entries oldest first; entries still in the write buffer used to come before
  older ones read from the file.
- The Hyper Testing guide skill is now `matrix-hyper-testing-guide`. It shared `matrix-hyper-testing` with
  the skill of the `hyper-testing` tool, so switching that tool overwrote or deleted the guide. The old
  guide folder is removed only when it holds that old guide, never a file the tool skill generator wrote.
- The `.gitignore` line Hyper Testing adds for its guide follows the skills folders actually in use
  instead of a fixed `.claude/skills` path.
- The version the editor reports to the server (`MatrixPlugin.Version`) matches the package again.

### Removed

- The unused SignalR connection layer and `IToolHostBuilder.Services`, `WithSkillFileGenerator<T>()`,
  `WithDynamicToolFactory()` and `WithConfigFromArgsOrEnv()`: the builder wires its parts directly.
- The `--url`, `--token` and `--auth` server flags, which nothing in the package passes.

## [0.87.1] - 2026-09-30

### Fixed

- Matrix Bridge and Matrix Space keep their font and rain after you press Play or open another scene.
- Matrix Bridge no longer floods the console with `MissingReferenceException`, and on Unity 6 its dock
  area switches tabs instead of throwing `InvalidOperationException`.
- An editor that already hit these errors recovers on the next script reload, with no restart.
- The Matrix rain no longer leaks a font and a material on every script reload.

## [0.87.0] - 2026-09-29

### Added

- Matrix Bridge window: one screen with a Matrix rain background that shows the bridge state, one
  click to link or unlink, and one click on a client name to wire or unwire its MCP configuration.
- **Rain** item in the Matrix Bridge `...` menu turns the rain background off for machines where it
  lags. It is the same setting as the Matrix Space rain toggle, so both windows follow it.

### Changed

- Matrix is now opt-in for each machine and project. Installing or updating the package no longer
  starts the server, links the editor, writes `.mcp.json` or skill files, or shows the Scene view
  toolbar, and Matrix windows or the toolbar restored from a saved layout close themselves. Turn it
  on with **Tools > Feeder > Matrix Setup**, or by clicking the state word in Matrix Bridge; click
  the state word again to turn it off. Existing installs turn it on once.
- The main window is now Matrix Bridge (**Tools > Feeder > Matrix Bridge**).
- Every connection is local: the bridge listens on loopback only, and a host outside loopback in the
  config is reset to the local default.
- Transport, authorization and timeout are set in `UserSettings/Feeder-MCP-Config.json` instead of
  the window.
- Individual tools are enabled or disabled in the same file or with the `tool-set-enabled-state`
  tool; the window no longer lists them.

### Removed

- Cloud connection mode and its authorization flow.
- The Tools, Prompts and Resources windows, and the built-in prompts and resource.

## [0.86.0] - 2026-09-29

- Added **Tools > Feeder > Matrix Setup**, a per-machine setup: it adds `.mcp.json` to the project's
  `.gitignore`, keeps only the core MCP tools enabled to cut the tokens sent with each request, and
  writes the Claude Code MCP config and installs or verifies the Claude Code CLI.
- Documented that `.mcp.json` holds a machine-specific port and should be gitignored, not committed.

## [0.85.0] - 2026-07-17

- Added zero-setup auto-configure: on editor load the package writes the MCP config file for
  enabled agents (default: Claude Code's project-root `.mcp.json`) and generates skill files,
  so a freshly installed package works without opening a window.
- Auto-configure self-heals stale ports when the project is cloned or moved to a new path
  (the server port derives from the project directory).
- Added a per-agent "Auto-configure on Unity load" toggle to the MCP status row;
  skill auto-generation now defaults to enabled for Claude Code.
- Auto-configure is skipped in batch mode, CI, and Cloud connection mode, and never blocks
  editor load on failure.
- Removed the external download/tutorial links from the agent configurator UI.

## [0.84.0] - 2026-07-16

- Replaced the bundled MCP server with the Feeder MCP Server 0.2.0.
- Added the FMP/1 Unity adapter while keeping the existing Unity tool registry and implementations.
- Preserved Streamable HTTP and stdio client compatibility in one self-contained executable.
- Preserved deferred tool completion across Unity compilation and domain reloads.
- Added loopback-only binding and optional bearer-token authentication for MCP and Unity links.
- Retained the historical executable filename only as a compatibility path for existing client configs.
- Compressed the self-contained server payload for Git distribution and added automatic, safe
  extraction into each consuming project's `Library/mcp-server` cache without requiring Git LFS.

## [0.83.0] - 2026-07-16

- Moved the package distribution to the standalone TheMatrix repository.
- Added Git URL installation documentation and release metadata.
- Replaced the external preferences package dependency with package-local typed preference wrappers
  that preserve existing preference keys.
- Moved the Feeder and NuGet managed DLLs used by the package out of FeederBase's `Assets`
  folder and into the distributable package.
- Added the Matrix persona as a built-in skill that can be generated for supported AI clients.
- Kept the Windows x64 local MCP server payload bundled with the package.

## [0.82.4]

- Initial standalone embedded package imported from FeederBase.
