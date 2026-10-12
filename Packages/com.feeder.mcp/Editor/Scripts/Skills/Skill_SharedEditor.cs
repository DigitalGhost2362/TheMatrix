#nullable enable
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiSkillType]
    public static class Skill_SharedEditor
    {
        public const string SkillId = "matrix-shared-editor";

        [AiSkill(SkillId,
@"Protocol for a Unity Editor shared by several AI sessions and humans at once: which Matrix tools need exclusive use of the Editor, the Temp/agent-editor-lock claim (atomic mkdir, heartbeat, stale break), the state check that catches a human in play mode, and the test-case handoff - when the Editor is busy the author writes TestCases/<stamp>-<slug>.md and stops, and a later verifier session runs the pending cases. Use BEFORE entering play mode, compiling, opening or saving a scene, wiping prefs or save data, clearing the console or running tests, and when asked to run pending test cases.")]
        public static string Markdown => @"
# Shared Editor

One Unity Editor per project, one MCP bridge, many sessions. Play mode, compilation, open scenes,
the console, EditorPrefs, PlayerPrefs and save files belong to the whole Editor, so two sessions that
test at the same time corrupt each other's result - and sometimes each other's assets. This skill
serializes Editor use and turns ""the Editor is busy"" into a written handoff instead of a collision.

Claim the Editor here before any compile or play session, then compile, read the console, play and
collect evidence the way the project's own workflow says.

The lock is a convention between sessions, not something Matrix enforces: a session that never reads
this skill ignores it. Tools that are not in your tool list may only be disabled: call
`unity-tool-list`, then `tool-set-enabled-state`.

## Rule 1 - What needs the lock

**Editor-exclusive** - claim the lock first:

- enter / exit play mode (`editor-application-set-state`)
- anything that triggers a compile: `script-update-or-create`, `script-delete`, `assets-refresh`
- `script-execute` that mutates anything, or that runs while you are playing
- `scene-open`, `scene-save`, `scene-unload`, prefab stage open/save, `assets-modify` / `assets-delete`
- wiping or changing PlayerPrefs, EditorPrefs or save files; changing `Time.timeScale`
- `console-clear-logs` (it deletes other sessions' evidence)
- `tests-run`, `package-add`, `package-remove`

**Free** - no lock:

- reading code, writing `.cs` or docs on disk with file tools
- read-only MCP queries: `editor-application-get-state`, `scene-list-opened`, `console-get-logs`,
  `gameobject-find`, `*-get-data`, screenshots

A file written on disk still reaches the Editor: Unity refreshes when its window regains focus, and
Hot Reload (if installed) patches a save straight into whoever is playing. You cannot prevent that,
which is why the verifier compares the tree before and after its run (Rule 5).

Never stop, pause or exit someone else's play mode. Never answer a modal dialog you did not open.

## Rule 2 - The lock

The lock is a directory: `<project>/Temp/agent-editor-lock/`, where `<project>` is the folder holding
`ProjectSettings/ProjectVersion.txt`. Creating a directory is atomic on every OS, so exactly one of two
racing sessions gets it. `Temp/` is gitignored, exists only while the Editor runs, and Unity deletes it
on quit - a lock left by a crashed Editor goes stale after the TTL. No `Temp/` means the Editor is closed: nothing to
claim, report the task as not verified.

Inside it, `lock.json`:

```json
{ ""owner"": ""claude-catwalk-score-1530"", ""task"": ""catwalk score rounding"", ""acquired"": ""2026-09-14T15:30:00+07:00"", ""heartbeat"": 1789367400 }
```

`owner` is `<agent>-<task-slug>-<HHmm>` - unique enough to recognize yourself. `heartbeat` is Unix
seconds. **TTL is 20 minutes**: a lock whose heartbeat is older than 1200 s is stale.

Acquire (bash / Git Bash):

```sh
cd ""<project>"" && mkdir Temp/agent-editor-lock 2>/dev/null \
  && printf '{""owner"":""%s"",""task"":""%s"",""acquired"":""%s"",""heartbeat"":%s}\n' \
       ""$OWNER"" ""$TASK"" ""$(date +%Y-%m-%dT%H:%M:%S%z)"" ""$(date +%s)"" > Temp/agent-editor-lock/lock.json \
  && echo acquired || cat Temp/agent-editor-lock/lock.json
```

Acquire (PowerShell - no `-Force`, so it fails when the directory exists):

```powershell
$l = ""<project>\Temp\agent-editor-lock""
try {
  New-Item -ItemType Directory $l -ErrorAction Stop | Out-Null
  @{ owner=$owner; task=$task; acquired=(Get-Date -Format o); heartbeat=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds() } |
    ConvertTo-Json -Compress | Set-Content ""$l\lock.json"" -Encoding utf8
  'acquired'
} catch { Get-Content ""$l\lock.json"" -ErrorAction SilentlyContinue }
```

- **Busy** (the directory exists): read `lock.json`. Live → the Editor is taken, go to Rule 4.
- **Stale** (`now - heartbeat > 1200`): re-read `lock.json` immediately before deleting and delete only
  if `heartbeat` is unchanged from what you judged stale; then acquire once. A lock directory with no
  `lock.json` is live (its owner is between `mkdir` and the write) unless the directory itself is older
  than 20 minutes. Never delete a live lock.
- **Heartbeat**: before every step that can take minutes (a compile, a play session, the next test
  case), re-read `lock.json`; if `owner` is still you, rewrite it with a fresh `heartbeat`. If `owner` is
  someone else, you lost the lock by going stale - stop all Editor-exclusive work and report.
- **Release**: exit play mode, restore what you changed (Rule 6), confirm `scene-list-opened` reports
  no scene dirtied by you, then delete the directory - only if `owner` is still you.

## Rule 3 - Check the Editor after acquiring

The lock only binds sessions that follow this skill. Humans do not. Right after acquiring, call
`editor-application-get-state` and `scene-list-opened`:

- `isPlaying`, `isPaused` or `isCompiling` true while you hold the lock → a human (or a rogue session)
  is using the Editor. Release and hand off (Rule 4).
- A dirty scene you did not touch → someone has unsaved work. Do not `scene-open` in Single mode or
  enter play mode over it; release and hand off.

A check one call earlier is not enough. Every mutating edit-mode `script-execute` still starts with:

```csharp
if (EditorApplication.isPlayingOrWillChangePlaymode) return ""abort: play mode"";
```

## Rule 4 - Author: finishing a task

When the task needs play-mode proof, try to acquire the lock.

- **Acquired and the Editor is clean** → verify normally (compile, read the
  console, play, collect evidence), release, report.
- **Busy, human in the Editor, or verification cut short** (focus trap, lost lock) → write the handoff
  and stop:
  1. Create `TestCases/` at the project root if missing, and make sure `.gitignore` ignores `/TestCases/`.
     Never put test cases under `Assets/` - a new file there triggers an import in the shared Editor.
  2. Write `TestCases/<yyyyMMdd-HHmm>-<slug>.md` from the *Test case template* at the end of this skill.
     One file per task, never append to another session's file. Status `pending`.
  3. Fill `Compiled` honestly: `no` if you did not get a clean console for this exact tree.
  4. Record every touched file with `git hash-object <path>`.
  5. Write steps a session with zero context can run through MCP: the tool, the arguments, the
     `script-execute` snippet or debug button, and the exact log line / state read / screenshot that
     proves it. ""Check that it works"" is not a step.
  6. Report: **written, NOT verified**, with the path to the test case. Do not wait for the Editor, do
     not compile, do not enter play mode.

If the project root has a `TESTCASE.md`, read it first - it holds that project's reset recipes and traps.

## Rule 5 - Verifier: running pending cases

Triggered by a request like ""run the pending test cases"".

1. List `TestCases/*.md` with `Status: pending`, oldest first. A `running` case whose owner holds no
   live lock was abandoned - treat it as `pending` and note that in its run log.
2. Acquire the lock (Rule 2) and check the Editor (Rule 3). Busy → report and stop.
3. Snapshot the tree: `git status --porcelain` and `git hash-object` of every touched file.
4. For each case:
   - Set `Status: running (<owner>, <time>)`. Heartbeat the lock.
   - Compare the recorded hashes with the current ones. Different → the code moved after the case was
     written: read the diff and adapt the steps before running them, and note it in the run log.
   - Compile and read the console. Filter `console-get-logs` by your own start
     time; do not clear the console.
   - Set the preconditions, run the steps, collect the evidence, then, when a panel changed, repeat it: open it twice in a row, leave a serialized reference empty, and invoke a callback that was never assigned.
   - **FAIL** → fix and re-run, at most **3 fix rounds**, then `Status: blocked`. Fix a file only if
     it is unchanged since your snapshot in step 3 **and** was not modified in the 10 minutes before
     it - otherwise another session may still be editing it: record the failure and the evidence,
     leave the code alone. Every fix follows the project's coding standard, is logged in the run log
     (file + reason) and gets its hash updated in the case header. Do not commit.
   - Before writing **PASS**, compare `git status --porcelain` with the step-3 snapshot plus your own
     fixes. Anything else changed during the run → the result is suspect; re-run the case once.
   - Clean up (Rule 6) and set `Status: pass`, `fail` or `blocked`, with the evidence.
5. Release the lock. Report per case: status, the evidence lines, the fixes made, anything not verified.

## Rule 6 - Leave the Editor as you found it

- Exit play mode.
- EditorPrefs: read the value before changing it, restore **that value** afterwards - do not delete a key
  you found set.
- PlayerPrefs / save files: back up before a wipe, restore after. A wipe is never the default.
- `Time.timeScale` back to what it was.
- Open scenes and the active scene as they were; nothing dirtied by you.
- If Enter Play Mode Options disable domain reload, static state survives between play sessions: one
  test leaks into the next. A case that needs first-boot state must say how to reset it.

## Rule 7 - Report honestly

A test case written is not a test run. Say ""written, NOT verified"" for a handoff, and for a verifier run
list exactly which cases ran, what proved each one, and which were skipped and why.

## Test case template

```markdown
# <Task title>

- **Status:** pending
- **Written:** <yyyy-MM-dd HH:mm> by <owner>
- **Branch @ HEAD:** <branch> @ <short sha> (plus uncommitted changes below)
- **Compiled:** yes, console clean | no - Editor was busy
- **Touched files** (`git hash-object`):
  - `<path>` - `<hash>`

## Why

What changed, in one paragraph, and what it could break. Name the flows a regression would show up in.

## Preconditions

- Scene(s) open, and which one is active:
- Prefs / save state (with how to back up and restore it):
- Config values or debug toggles:
- Anything from the project's `TESTCASE.md` reset recipe that applies:

## Cases

### TC1 - <what is proven>

- **Steps:**
  1. `<mcp tool>` with `<args>` / `script-execute`: `<snippet>` / debug button `<Class.Method>`
- **Expect:** `<exact log line>` / `<state read and value>` / screenshot showing `<what>`
- **Result:** _

### TC2 - <edge case: re-entry, missing reference, empty data, second call>

- **Steps:**
- **Expect:**
- **Result:** _

## Standing passes

Only when a panel changed: open twice in a row, empty serialized reference, unassigned callback.

## Cleanup

What to restore after the run: prefs, save file, timeScale, scenes.

## Not covered

What these cases cannot prove (real SDK, device-only, multi-day timer) - so nobody reads PASS as more
than it is.

## Run log

<!-- verifier appends: time, owner, per-case PASS/FAIL/BLOCKED + evidence, fixes (file + reason) -->
```
";
    }
}
