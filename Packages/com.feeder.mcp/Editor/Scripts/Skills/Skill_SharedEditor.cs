#nullable enable
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiSkillType]
    public static class Skill_SharedEditor
    {
        public const string SkillId = "matrix-shared-editor";

        [AiSkill(SkillId,
@"Protocol for a Unity Editor shared by several AI sessions and humans: which Matrix tools change the Editor, the editor lock (implicit claim freed after 30 idle seconds; explicit editor-lock-acquire only for a multi-step burst such as a play session), the human-in-play-mode check, and the TestCases handoff when the Editor is busy. Use BEFORE entering play mode, compiling, opening or saving a scene, wiping prefs, running tests, or when asked to run pending test cases.")]
        public static string Markdown => @"
# Shared Editor

One Unity Editor, one MCP bridge, many sessions. Play mode, compilation, open scenes, console, prefs and
save files are shared, so concurrent tests corrupt each other. Matrix serializes Editor use; when the
Editor is busy you write a handoff instead of colliding.

## Default: just call the tool

- An editor-changing tool (`assets-refresh`, `scene-save`, `scene-open`, `rql-mutate`, mutating
  `script-execute`, `tests-run`, ...) called while nobody holds the lock runs and takes an **implicit
  claim**, freed after **30 s** without a call from you. No `editor-lock-acquire` for one compile, one
  scene save or one `script-execute` edit.
- If someone else holds the Editor, the tool waits in line (~50 s). If it answers `lock_queued` (""The tool
  did not run"") call **the same tool again at once** to keep your place.
- Read-only tools (`rql-query`, `console-get-logs`, `editor-health`, screenshots, `editor-lock-status`) never
  wait. A `script-execute` counts as editor-changing even if it only reads: read with `rql-query` instead.
- Do not poll `editor-lock-status` in a loop; do not acquire ""just in case"".
- Edit `.cs` files with your own file tools (free), **batch the edits, then one `assets-refresh`**, then read the console.
- A tool missing from your list may be disabled: `unity-tool-list`, then `tool-set-enabled-state`.

## Explicit lock: only for a burst that must not be interleaved

Above all a **play session**: enter play -> test -> exit. Acquire right before, release right after.

- `editor-lock-acquire` (`task`, `owner` = `<agent>-<slug>`). Free -> `acquired` plus Editor state. Taken -> waits
  ~50 s in a first-come queue, then answers `queued` (not an error) with a `ticket`: call again at once with
  that `ticket`. `wait:false` answers `busy:` with the holder instead.
- `editor-lock-release` frees only your own claim; `done:true` also frees agents waiting on you with `after`.
- `editor-lock-status`: holder, queue, your position.
- `after:[""<owner>""]` on acquire: wait for that agent to finish (it releases with `done:true`, ends, or its
  session closes); no polling. Give dependent subagents their `owner` label in their prompt.
- Release before anything that needs no Editor: thinking, coding, Blender, subagents, reviewers, the user,
  writing reports or test cases. A review loop claims once per burst, never across the loop.
- **Cap 15 min per claim.** Past it every tool result ends with an `Editor lock:` line; do what it says. Every new
  claim restarts with the Rule 1 check. Idle claims are freed by the server; you only see the outcomes below.
- Releasing with your play mode still on: acquire again within about a minute to exit it, else the next in line gets it.
- With the Matrix hook a subagent is an agent of its own (own queue place, claim ends with it). A main agent that
  waits for its subagent passes its claim on: release before spawning.
- When the user asks you to stop: release at once and do no Editor-exclusive work until told otherwise. A person
  can free a claim from the Matrix Bridge window.

Outcomes:

- **queued** -> acquire again with the ticket. **busy**, a human in play mode, or the user's wait limit -> Rule 3.
- **Refused ""held by ...""** or **release says ""already freed""** -> your claim was lost; stop Editor work, treat the
  burst's evidence as suspect, `editor-lock-status`, restart from a clean check.
- **Lost the lock during play** -> acquire (the play owner goes first), exit play mode, rerun the session from the start.
- **Lock tools missing** -> the server is older than the package: tell the user to reinstall it from the Matrix Bridge menu.

## Rule 1 - Safety

- Check state with `editor-health` (always available, works during compile/reload): phase playing / paused /
  compiling. `editor-application-get-state` is not on by default.
- After a fresh claim also `rql-query` `scenes`. Playing, paused or compiling that you did not cause -> a human is
  using the Editor: release, hand off.
- A dirty scene in an idle Editor (edit mode, not compiling) is not a reason to stop: Unity marks scenes dirty on
  its own (a layout or scrollbar driving a RectTransform when the Game view resizes). `scene-save` with `all`, then
  carry on. Only an untitled dirty scene cannot be saved: leave it open, work around it, hand off if the task needs it closed.
- Never pass `ifDirty=discard` for work that is not yours.
- Never stop, pause or exit someone else's play mode. Exception: `acquired` says ""in play mode left on by
  <agent>"" - that agent is gone; exit it first. Never answer a modal dialog you did not open.
- A mutating edit-mode `script-execute` starts with `if (EditorApplication.isPlayingOrWillChangePlaymode) return ""abort: play mode"";`
- A file written to disk still reaches the Editor (focus refresh, Hot Reload); Matrix cannot refuse that.

## Rule 2 - Finishing a task

For play-mode proof acquire the lock (queued -> retry with the ticket). Clean Editor: compile, read the console,
play, collect evidence, release. If a result sends you back to code, release first, fix, claim again.

## Rule 3 - Handoff (human in the Editor, wait limit exceeded, or verification cut short)

1. Create `TestCases/` at the project root if missing; `.gitignore` must ignore `/TestCases/`; never under `Assets/`.
2. Write `TestCases/<yyyyMMdd-HHmm>-<slug>.md` from the template below, Status `pending`; one file per task,
   never append to another session's file.
3. `Compiled`: `no` unless the console was clean for this exact tree. Record touched files with `git hash-object`.
4. Steps a zero-context session can run: tool, arguments, `script-execute` snippet or debug button, and the exact
   log line / state / screenshot that proves it. ""Check that it works"" is not a step.
5. Report **written, NOT verified** with the path. Do not wait, compile or enter play mode.

If the project root has `TESTCASE.md`, read it first (reset recipes, traps).

## Rule 4 - Verifier: ""run the pending test cases""

1. List `Status: pending` cases, oldest first (a `running` case whose owner holds no claim is abandoned: treat as pending, note it).
2. Snapshot `git status --porcelain` and the hashes. Acquire, then the Rule 1 check. One claim per case.
3. Per case: set `running (<owner>, <time>)`. Hashes differ -> read the diff, adapt the steps, note it. Compile and
   read the console filtered from the claim start (never clear it). Run the steps and collect evidence; if a panel
   changed also open it twice in a row, leave a serialized reference empty, invoke a never-assigned callback.
4. **FAIL**: exit play, restore, release while you fix, re-claim; max 3 fix rounds, then `blocked`. Fix a file only if
   it is unchanged since the snapshot and not modified in the last 10 min; otherwise record the evidence and leave
   it. Log each fix (file + reason), update its hash, follow the project coding standard, do not commit.
5. Before **PASS**, `git status --porcelain` must equal the snapshot plus your fixes, else re-run once.
6. Restore (Rule 5), set `pass|fail|blocked` with evidence, release. Report per case: status, evidence, fixes, unverified.

## Rule 5 - Leave the Editor as found

Exit play mode. EditorPrefs: restore the value you read, never delete a key you found set. PlayerPrefs / saves: back
up before a wipe (never the default), restore after. `Time.timeScale` back. Scenes and active scene as before,
nothing dirtied by you. With domain reload off, static state leaks between plays: a case needing first-boot state
says how to reset it. Report honestly: ""written, NOT verified"" for a handoff; for a run, what ran, what proved it,
what was skipped.

## Rule 6 - Stuck Editor and long jobs

- Timeout or no answer: `editor-health` (the server answers even while Unity hangs). Reloading / compiling /
  importing -> wait, call again. Slow and unfocused -> not a hang. Stalled, nothing in flight -> a dialog or another
  package: tell the user, do not remove packages or answer dialogs. Stalled ""Likely cause: <tool>"" -> your own call:
  wait or ask. Not linked -> Editor closed; report not verified.
- Console warning `[Matrix Bridge] Cleared an agent play mark ...`: another package's play mode handler throws;
  report that exception, do not fix that package.
- Long jobs: `script-execute` with `timeoutSeconds`, or schedule from `EditorApplication.update` and write a marker
  under `Temp/` (`started|done|failed`); poll the marker. Read it before rescheduling: a dropped connection or a
  reload does not mean the job did not run.

## Test case template

```markdown
# <Task title>

- **Status:** pending
- **Written:** <yyyy-MM-dd HH:mm> by <owner>
- **Branch @ HEAD:** <branch> @ <short sha> (plus uncommitted changes)
- **Compiled:** yes, console clean | no - Editor was busy
- **Touched files** (`git hash-object`): `<path>` - `<hash>`

## Why
What changed, what it could break.

## Preconditions
Scenes open / active; prefs and save state (backup + restore); config or debug toggles; TESTCASE.md recipe.

## Cases
### TC1 - <what is proven>
- **Steps:** 1. `<tool>` with `<args>` / `script-execute`: `<snippet>` / debug button `<Class.Method>`
- **Expect:** `<exact log line>` / `<state and value>` / screenshot showing `<what>`
- **Result:** _
### TC2 - <edge: re-entry, missing reference, empty data>

## Cleanup
Prefs, save file, timeScale, scenes to restore.

## Not covered
What PASS does not prove.

## Run log
<!-- verifier appends: time, owner, per-case result + evidence, fixes (file + reason) -->
```
";
    }
}
