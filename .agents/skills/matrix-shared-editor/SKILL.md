---
name: matrix-shared-editor
description: "Protocol for a Unity Editor shared by several AI sessions and humans at once: which Matrix tools need exclusive use of the Editor, the Temp/agent-editor-lock claim (atomic mkdir, heartbeat, stale break), the state check that catches a human in play mode, and the test-case handoff - when the Editor is busy the author writes TestCases/<stamp>-<slug>.md and stops, and a later verifier session runs the pending cases. Use BEFORE entering play mode, compiling, opening or saving a scene, wiping prefs or save data, clearing the console or running tests, and when asked to run pending test cases."
---

# Shared Editor

One Unity Editor per project, one MCP bridge, many sessions. Play mode, compilation, open scenes,
the console, EditorPrefs, PlayerPrefs and save files belong to the whole Editor, so two sessions that
test at the same time corrupt each other's result - and sometimes each other's assets. This skill
serializes Editor use and turns "the Editor is busy" into a written handoff instead of a collision.

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
{ "owner": "claude-catwalk-score-1530", "task": "catwalk score rounding", "acquired": "2026-09-14T15:30:00+07:00", "heartbeat": 1789367400 }
```

`owner` is `<agent>-<task-slug>-<HHmm>` - unique enough to recognize yourself. `heartbeat` is Unix
seconds. **TTL is 20 minutes**: a lock whose heartbeat is older than 1200 s is stale.

Run the lock scripts from the project root. Set `OWNER` and `TASK` first (plain words, no quotes).

POSIX shell (Git Bash on Windows, macOS, Linux):

```sh
lock=Temp/agent-editor-lock
lock_acquire() {
  [ -d Temp ] || { echo "no Temp/: the Editor is closed"; return 1; }
  if mkdir "$lock" 2>/dev/null; then
    printf '{"owner":"%s","task":"%s","acquired":"%s","heartbeat":%s}\n' \
      "$OWNER" "$TASK" "$(date +%Y-%m-%dT%H:%M:%S%z)" "$(date +%s)" > "$lock/lock.json"
    echo acquired
  else
    cat "$lock/lock.json" 2>/dev/null; echo busy; return 1
  fi
}
lock_heartbeat() {
  grep -q "\"owner\":\"$OWNER\"" "$lock/lock.json" 2>/dev/null || { echo "lost the lock"; return 1; }
  sed -i.bak "s/\"heartbeat\":[0-9]*/\"heartbeat\":$(date +%s)/" "$lock/lock.json" && rm -f "$lock/lock.json.bak"
  echo beat
}
lock_release() {
  grep -q "\"owner\":\"$OWNER\"" "$lock/lock.json" 2>/dev/null || { echo "not mine, left alone"; return 1; }
  rm -rf "$lock" && echo released
}
lock_break_stale() {
  beat=$(sed -n 's/.*"heartbeat":\([0-9]*\).*/\1/p' "$lock/lock.json" 2>/dev/null)
  [ -n "$beat" ] && [ $(( $(date +%s) - beat )) -gt 1200 ] || { echo "not stale"; return 1; }
  # delete only if nobody beat in between
  [ "$(sed -n 's/.*"heartbeat":\([0-9]*\).*/\1/p' "$lock/lock.json")" = "$beat" ] && rm -rf "$lock" && echo "stale lock removed"
}
```

PowerShell (Windows PowerShell 5.1 and later):

```powershell
$lock = 'Temp\agent-editor-lock'
function Write-Lock($data) { [IO.File]::WriteAllText((Join-Path (Get-Location) "$lock\lock.json"), ($data | ConvertTo-Json -Compress)) }
function Read-Lock { try { Get-Content "$lock\lock.json" -Raw -ErrorAction Stop | ConvertFrom-Json } catch { $null } }
function Lock-Acquire {
  if (-not (Test-Path Temp)) { 'no Temp/: the Editor is closed'; return }
  try { New-Item -ItemType Directory $lock -ErrorAction Stop | Out-Null } catch { Read-Lock; 'busy'; return }
  Write-Lock ([ordered]@{ owner = $owner; task = $task; acquired = (Get-Date -Format o); heartbeat = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() })
  'acquired'
}
function Lock-Heartbeat {
  $data = Read-Lock
  if ($null -eq $data -or $data.owner -ne $owner) { 'lost the lock'; return }
  $data.heartbeat = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds(); Write-Lock $data; 'beat'
}
function Lock-Release {
  $data = Read-Lock
  if ($null -eq $data -or $data.owner -ne $owner) { 'not mine, left alone'; return }
  Remove-Item $lock -Recurse -Force; 'released'
}
function Lock-BreakStale {
  $data = Read-Lock
  if ($null -eq $data -or [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - $data.heartbeat -le 1200) { 'not stale'; return }
  # delete only if nobody beat in between
  if ((Read-Lock).heartbeat -eq $data.heartbeat) { Remove-Item $lock -Recurse -Force; 'stale lock removed' }
}
```

`New-Item` without `-Force` fails when the directory exists, which is what makes the claim atomic.

- **Busy** (the directory exists): read `lock.json`. Live -> the Editor is taken, go to Rule 4.
- **Stale** (`now - heartbeat > 1200`): re-read `lock.json` immediately before deleting and delete only
  if `heartbeat` is unchanged from what you judged stale; then acquire once. A lock directory with no
  `lock.json` is live (its owner is between `mkdir` and the write) unless the directory itself is older
  than 20 minutes. Never delete a live lock.
- **Heartbeat** (`lock_heartbeat` / `Lock-Heartbeat`): before every step that can take minutes (a
  compile, a play session, the next test case), re-read `lock.json`; if `owner` is still you, rewrite it
  with a fresh `heartbeat`. If `owner` is
  someone else, you lost the lock by going stale - stop all Editor-exclusive work and report.
- **Release**: exit play mode, restore what you changed (Rule 6), confirm `scene-list-opened` reports
  no scene dirtied by you, then delete the directory - only if `owner` is still you.

## Rule 3 - Check the Editor after acquiring

The lock only binds sessions that follow this skill. Humans do not. Right after acquiring, call
`editor-application-get-state` and `scene-list-opened`:

- `isPlaying`, `isPaused` or `isCompiling` true while you hold the lock -> a human (or a rogue session)
  is using the Editor. Release and hand off (Rule 4).
- A dirty scene you did not touch -> someone has unsaved work. Do not `scene-open` in Single mode or
  enter play mode over it; release and hand off.

A check one call earlier is not enough. Every mutating edit-mode `script-execute` still starts with:

```csharp
if (EditorApplication.isPlayingOrWillChangePlaymode) return "abort: play mode";
```

## Rule 4 - Author: finishing a task

When the task needs play-mode proof, try to acquire the lock.

- **Acquired and the Editor is clean** -> verify normally (compile, read the
  console, play, collect evidence), release, report.
- **Busy, human in the Editor, or verification cut short** (focus trap, lost lock) -> write the handoff
  and stop:
  1. Create `TestCases/` at the project root if missing, and make sure `.gitignore` ignores `/TestCases/`.
     Never put test cases under `Assets/` - a new file there triggers an import in the shared Editor.
  2. Write `TestCases/<yyyyMMdd-HHmm>-<slug>.md` from the *Test case template* at the end of this skill.
     One file per task, never append to another session's file. Status `pending`.
  3. Fill `Compiled` honestly: `no` if you did not get a clean console for this exact tree.
  4. Record every touched file with `git hash-object <path>`.
  5. Write steps a session with zero context can run through MCP: the tool, the arguments, the
     `script-execute` snippet or debug button, and the exact log line / state read / screenshot that
     proves it. "Check that it works" is not a step.
  6. Report: **written, NOT verified**, with the path to the test case. Do not wait for the Editor, do
     not compile, do not enter play mode.

If the project root has a `TESTCASE.md`, read it first - it holds that project's reset recipes and traps.

## Rule 5 - Verifier: running pending cases

Triggered by a request like "run the pending test cases".

1. List `TestCases/*.md` with `Status: pending`, oldest first. A `running` case whose owner holds no
   live lock was abandoned - treat it as `pending` and note that in its run log.
2. Acquire the lock (Rule 2) and check the Editor (Rule 3). Busy -> report and stop.
3. Snapshot the tree: `git status --porcelain` and `git hash-object` of every touched file.
4. For each case:
   - Set `Status: running (<owner>, <time>)`. Heartbeat the lock.
   - Compare the recorded hashes with the current ones. Different -> the code moved after the case was
     written: read the diff and adapt the steps before running them, and note it in the run log.
   - Compile and read the console. Filter `console-get-logs` by your own start
     time; do not clear the console.
   - Set the preconditions, run the steps, collect the evidence, then, when a panel changed, repeat it: open it twice in a row, leave a serialized reference empty, and invoke a callback that was never assigned.
   - **FAIL** -> fix and re-run, at most **3 fix rounds**, then `Status: blocked`. Fix a file only if
     it is unchanged since your snapshot in step 3 **and** was not modified in the 10 minutes before
     it - otherwise another session may still be editing it: record the failure and the evidence,
     leave the code alone. Every fix follows the project's coding standard, is logged in the run log
     (file + reason) and gets its hash updated in the case header. Do not commit.
   - Before writing **PASS**, compare `git status --porcelain` with the step-3 snapshot plus your own
     fixes. Anything else changed during the run -> the result is suspect; re-run the case once.
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

A test case written is not a test run. Say "written, NOT verified" for a handoff, and for a verifier run
list exactly which cases ran, what proved each one, and which were skipped and why.

## Rule 8 - A stuck Editor

When a tool times out, or the Editor stops answering, do not guess. Call `editor-health`: the server
answers it itself, even while the Editor is hung or reloading. Read its first line and the window list.

- **Reloading** or **Busy: compiling / importing** -> wait and call it again; the Editor comes back.
- **Ticking slowly, Editor not focused** -> not a hang; calls are only slower.
- **Main thread stalled ... in flight: none** -> something outside Matrix holds the Editor: a dialog, an
  import, another package. The window list names it. Tell the user what it says and wait for them; do
  not switch off or remove packages of the project, and do not answer a dialog you did not open.
- **Main thread stalled ... Likely cause: <tool>** -> your own call holds it (a long or endless
  `script-execute`). Wait for it, or ask the user before going further.
- **Not linked** -> the Editor is closed or Matrix is off; report the task as not verified.

## Rule 9 - Long jobs

A tool call ends at the project's tool timeout. For work that takes longer:

- `script-execute` with `timeoutSeconds` for code that runs long on purpose (the server caps it, 30
  minutes by default).
- Or start the job inside Unity and come back for the result. Schedule it from `EditorApplication.update`
  (it runs while the Editor is in the background; `EditorApplication.delayCall` does not), remove the
  callback when it ran, and write a marker file under `Temp/` with the job's name and state
  (`started`, `done`, `failed`). Poll the marker, not the connection.
- Before you schedule a job again, read its marker and `editor-application-get-state`. A dropped
  connection or a domain reload does not mean the job did not run: never schedule it a second time
  because the link went away.

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
