# Matrix Bridge

Matrix Bridge exposes Unity Editor tools to MCP-compatible AI
clients. The package ships with the local Windows x64 MCP server payload.

## Installation

In Unity 2022.3 or newer, open **Window > Package Manager**, choose
**Add package from git URL**, and use:

```text
https://github.com/BingoBoiz/TheMatrix.git?path=/Packages/com.feeder.mcp
```

The package is plain source: it contains no precompiled `.dll`. It ships a
compressed self-contained local server. On first use, the editor extracts the server into the
consuming project's `Library/feeder-matrix` cache. It does not require Git LFS or a separate .NET
runtime. Unity resolves the official Unity Test Framework dependency from its standard registry.

For reproducible installs, use a released tag:

```text
https://github.com/BingoBoiz/TheMatrix.git?path=/Packages/com.feeder.mcp#v0.87.0
```

## Getting started

Installing the package changes nothing on a machine: no window opens, no server starts and no file
is written until the person using the project turns Matrix on. Teammates who do not use MCP are
never affected, even when the project is shared through version control.

To turn it on, run **Tools > Feeder > Setup...** once (see [Matrix Setup](#matrix-setup)). Until
then the other items of the **Tools > Feeder** menu are greyed out. The choice is stored per
machine and per project folder, outside the project, so it is never committed. A saved window
layout that includes Matrix windows or the Scene view toolbar (for example one checked in with the
project) does not open them for someone who has not turned Matrix on.

Once Matrix is on, the package does this each time the editor loads:

1. Extracts and starts the bundled local MCP server.
2. Writes the project-root `.mcp.json` with the correct server URL (Claude Code picks it up on
   the next session in that folder).
3. Generates skill files into `.claude/skills`, one `matrix-<tool>` folder for each enabled tool.

For other AI clients, open **Tools > Feeder > Bridge** and click the client name at the
bottom of the window to wire or unwire it. To turn Matrix off again, click the state word while it
reads ONLINE: the server stops, the other **Tools > Feeder** items grey out again, and nothing
starts, opens or is written on the next load.

Note: the server port is derived from the project's directory path, so `.mcp.json` differs
between machines that clone the project to different paths. The file is rewritten with the local
port on editor load. Do not commit it: run **Tools > Feeder > Setup...** once per machine to
gitignore it.

## Matrix Bridge window

Open **Tools > Feeder > Bridge**. Every connection is local: the bridge listens on loopback
only.

- Click the large state word to link or unlink (OFFLINE, LINKING, ONLINE).
- Click a client name to write or remove its MCP configuration. A dot under the name means it is
  wired; an amber dot means the configuration is outdated and a click rewrites it. The `+N` entry
  lists more clients.
- The `...` menu copies the endpoint, changes the port while offline, sets the logging level,
  opens the Skills window, turns the rain background on or off (the same setting as in Matrix
  Space, for machines where it lags), opens the logs and the config file, and reinstalls the server.
- The window's tab icon dims while the editor is not linked.

## Skills window

Open **Tools > Feeder > Skills**. It lists every MCP tool with its own switch.

- Pick a group on the left or type in the search box; the All, On and Off buttons filter by state.
- Each row shows what the tool does, the tokens it adds to every request while it is on, and a
  `writes` or `destructive` tag when it changes the project. When a tool works with another one that
  is off, the row says so; click the name to switch it on too.
- The bar at the bottom adds up the tokens of everything that is on. **Core only** returns to the
  default set. **Enable all** asks first, because it costs about 50k tokens per request.
- A change rewrites the skill files after a second. Reconnect the AI client so it lists the new set.

## Matrix Setup

Run **Tools > Feeder > Setup...** once on each machine. It:

- turns Matrix on for the project on this machine: it starts the local server and links the editor;
- adds `.mcp.json` to the project's `.gitignore`; if the file is already committed, it prints
  `git rm --cached .mcp.json` for you to run;
- keeps only the core MCP tools enabled and disables the rest, to cut the tokens sent with every
  request; switch more on in **Tools > Feeder > Skills**, or a client enables them on demand with
  `tool-set-enabled-state`;
- writes the Claude Code MCP config, then installs or verifies the Claude Code CLI (Node.js is
  installed with winget if missing).

## Hyper Testing Mode (Experimental)

Several clone editors play test one project in parallel, each driven by its own `hyper-playtester-N`
subagent. It is off by default, Windows only, and only a person can turn it on, from
**Tools > Feeder > Hyper Testing (Experimental)**. The switch is per machine and per project, and turns off
again on every package update.

- **Clones:** `up` creates the clones in `<project>.HyperClones/hc<N>`. `Assets` and the embedded packages
  are junctions to the project, and each clone has its own `Library` (without the Search index) and server
  port. The clones start one after another behind a commit gate and a VRAM gate, and open no scene on start.
- **Memory:** Windows runs out of commit, not of free RAM, so `up` refuses when the commit limit cannot hold
  one more editor plus `ramReserveMb`, and the 30 s sampler writes commit and import workers to
  `HyperTesting/Journal/ram-samples.csv`. Clones import in their own process (`cloneImportWorkers` 0 in
  `HyperTesting/config.json`) and a GPU with 4096 MB or less starts one clone.
- **Writes into the project:** a clone that creates an asset creates it in the project. The clone reports
  every asset it creates as CRITICAL, and `up` takes a git baseline so `down` and `status` can warn about new
  untracked paths. Packages that write assets under `Assets/` when play starts do not work with clones.
- **Driving a clone by hand:** `HyperTesting/tools/` holds `hc.py`, `hx.sh`, `hxf.sh` and `shot.py`, which
  call a clone over HTTP when a play tester has no MCP tools for it.
- **Refreshing:** after a code change, `refresh` recompiles the clones one at a time.
- **Running cases:** the agent following the `matrix-hyper-testing` skill splits test cases over the
  clones and spawns the play testers in parallel.
- **Results and journal:** results go to `HyperTesting/Runs/`, and the journal goes to
  `HyperTesting/Journal/`.
- **Removing clones:** use the window's **Delete clones** button. Never delete a clone folder
  recursively by hand: its `Assets` is a junction to the project.

Restart Claude Code once after the first enable so it picks up `.claude/agents/`.

## Platform support

The package currently includes the local server binary for Windows x64. The Unity editor code
can be imported on other platforms, but starting the bundled server requires a matching payload
under `Server~/<platform>-<architecture>`.

The Windows payload is the Feeder MCP Server, installed as `Library/feeder-matrix/win-x64/matrix-server.exe`
and reachable only on `127.0.0.1`. The Git package stores this payload as `server-payload.zip` to
remain below GitHub's per-file limit; installation and updates are handled automatically by the
editor package. Updating from 0.87 or earlier stops the project's old Matrix server, removes the files
it left in `Library` (only when they are Matrix's own), and rewrites the config of every wired agent;
restart the agent session afterwards.

## Names Matrix uses

Matrix only creates, edits and deletes things under names of its own:

- the server lives in `Library/feeder-matrix` and the log in `Temp/feeder-matrix`;
- its entry in `.mcp.json` and the other client configs is `Feeder-MCP`, and its environment
  variables start with `FEEDER_MATRIX_`;
- its skill folders are `matrix-<tool>`;
- its data types are in the `Feeder.MCP.Data` namespace;
- it never edits or deletes a server, skill or config entry that it did not write.

## License

MIT. See `LICENSE.md`.
