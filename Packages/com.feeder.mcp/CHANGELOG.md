# Changelog

All notable changes to this package are documented here.

## [Unreleased]

### Added

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

### Changed

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

### Fixed

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
