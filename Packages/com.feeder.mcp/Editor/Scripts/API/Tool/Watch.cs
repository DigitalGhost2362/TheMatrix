#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using Feeder.MCP.Editor.Watch;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Watch
    {
        public const string WatchToolId = "watch";
        const int MaxSummaryChars = 12000;

        [AiTool
        (
            WatchToolId,
            Title = "Editor / Watch Playtest",
            Enabled = false
        )]
        [AiSkillDescription("Learn a playtest workflow by watching the user play it, then replay it fast: `watch start` puts a cyan frame on the Game View and records every input, UI click (object path, label, listeners), player position, scene change, console error and a frame every 2 s until the user exits Play mode or presses STOP; the user then describes the workflow in chat. Holds the read-ask-playbook-replay loop: read summary.md and contact sheets, read the code behind the clicks, ask only what is missing, write a playbook, replay with code shortcuts, UI clicks, walking and teleports only for long distances, and report stuck or fallen players. Use when the user invokes matrix-watch or asks to record, show, teach or replay a playtest flow.")]
        [AiSkillBody(SkillBody)]
        [Description("Record the user's playtest in the Game View. action = start | stop | status. " +
            "start arms recording (cyan frame); it runs in Play mode and ends when the user exits Play mode or presses STOP. " +
            "status returns the live state, or the latest finished session's summary.md.")]
        public string Run
        (
            [Description("start | stop | status")]
            string action = "status",
            [Description("start: short workflow name used in the session folder, e.g. 'buy-shop-item'.")]
            string? name = null,
            [Description("start: hierarchy path or name of the player object to track. Empty = tag 'Player', then the first CharacterController.")]
            string? player = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                switch ((action ?? "status").Trim().ToLowerInvariant())
                {
                    case "start":
                        return Start(name ?? string.Empty, player ?? string.Empty);
                    case "stop":
                        return Stop();
                    case "status":
                        return Status();
                    default:
                        return $"Unknown action '{action}'. Use start, stop or status.";
                }
            });
        }

        static string Start(string name, string player)
        {
            if (WatchSession.IsArmed)
                return $"Already watching into {WatchSession.Folder}. Wait for the user to stop it, or call action=stop.";
            var folder = WatchSession.Arm(name, player);
            var state = EditorApplication.isPlaying ? "Recording now (already in Play mode)." : "Armed: recording starts when the user enters Play mode.";
            return $"{state}\nSession: {folder}\n" +
                   "Tell the user, in their language: press Play and play the workflow; F9 marks an important moment; " +
                   "exit Play mode or press STOP on the cyan frame to finish; then describe in chat what the workflow is for and what to watch. " +
                   "Do not touch the Editor while they play. When they say they are done, call watch action=status.";
        }

        static string Stop()
        {
            if (!WatchSession.IsArmed)
                return "Nothing is being watched. " + Latest();
            var folder = WatchSession.Stop(WatchSession.ReasonAgent);
            return folder == null ? "Stopped. Nothing was recorded (Play mode never started)." : Finished(folder);
        }

        static string Status()
        {
            if (!WatchSession.IsArmed)
                return Latest();
            var recorder = WatchSession.Recorder;
            if (recorder == null)
                return $"Armed, waiting for the user to enter Play mode.\nSession: {WatchSession.Folder}";
            return $"Recording: {recorder.Now:0}s, {recorder.EventCount} events, {recorder.FrameCount} frames.\nSession: {WatchSession.Folder}\n" +
                   "The user has not stopped yet (exit Play mode or STOP). Do not stop it yourself unless they ask.";
        }

        static string Latest()
        {
            var folder = WatchSession.LatestFolder();
            return folder == null ? "No finished watch session in this project." : Finished(folder);
        }

        static string Finished(string folder)
        {
            var summary = File.ReadAllText(Path.Combine(folder, WatchSummary.SummaryFile));
            var cut = summary.Length > MaxSummaryChars;
            return $"Session: {folder}\n\n" + (cut ? summary.Substring(0, MaxSummaryChars) + "\n... (cut; read summary.md for the rest)" : summary);
        }

        const string SkillBody = @"# Matrix Watch

The Architect plays a workflow once while you watch; you learn it, ask what is missing, and replay it
faster than a human can. Recording is the user's; replaying is yours.

## 1. Record

1. `watch action=start name=<short-slug>` (add `player=<path>` when you already know the player object).
   A cyan frame tagged `WATCHING THE ARCHITECT` with a STOP button appears on the Game View.
2. Tell the user, in their language: press Play, play the workflow; **F9** marks an important moment;
   exit Play mode or press **STOP** to finish (whichever comes first ends the recording); then describe
   in chat what the workflow is for, what matters, what counts as a bug.
3. While they play: hold no editor lock, call no Editor tool, enter no play mode. Wait for their message.
4. `watch action=status` returns `summary.md` of the finished session. If it still says Recording, they
   have not stopped; ask, do not stop it yourself.

## 2. Read, cheapest first

| Order | File | Cost |
|---|---|---|
| 1 | `summary.md` (returned by status): timeline, flags, safe points, sheet index | text |
| 2 | `sheets/sheet-NN.jpg`: 3x3 frames of the moments that matter, left-right, top-bottom | ~1.2k tokens each |
| 3 | `frames/fNNNN.jpg`: one 640 px frame; open only for markers, errors, clicks you cannot place | ~0.4k each |
| 4 | `events.jsonl`: raw events; grep a time window (`""t"":4`), never read it whole | text |

Timeline lines: `click ui /Canvas/Shop/Item[2]/BtnBuy ""Buy"" -> ShopItem.OnBuy [ShopItemView] @(0.52,0.31) f0012`
= hierarchy path (`[i]` = index among same-named siblings), label, persistent listeners, game components,
normalized screen point, frame. `move A -> B 67m 21s keys W,D` folds walking. `STUCK` / `FELL` /
`position jumped` flags are candidate bugs. Listeners added in code are not shown: open the scripts of the
listed components and find the handler.

Then read the code: each clicked handler, the player controller (how it reads input, how it moves), and
whatever the user's description points at. Use `rql-query` for live state when the Editor is free.

## 3. Ask before building

At most 3-5 questions per round, only what code and recording cannot answer:
- which phases are under test and which are only setup (setup may be skipped with code shortcuts);
- what pass means (panel open, value changed, quest state, no error);
- reset needed before each run (save file, PlayerPrefs, scene);
- allowed cheats (time scale, currency, skip tutorial) and randomness to pin.

## 4. Playbook

Write `<session>/playbook.md`: goal, preconditions, then one row per step:
`# | step | how | check | evidence frame`. For each step pick the fastest way that still tests what is
under test:

1. **Code call** for setup that is not under test (grant item, set flag, load scene).
2. **UI click by path**: wait until the object is active and interactable, then
   `ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler)`.
3. **Walk** the recorded path (waypoints every 5-8 m and at turns) through the game's own controller.
4. **Teleport** only when the remaining path is longer than 40 m or 15 s of walking: jump to a recorded
   safe point (summary `Safe points`) 10-15 m before the target, then **walk the rest**, because stuck and
   fall-through bugs show up near targets. Never teleport over a stretch the user called under test.
5. **Raw input** only for timing-heavy moves (combos, drags, rhythm).

Speed: `Time.timeScale` 2-4 while walking or waiting, 1 for steps under test or physics-sensitive ones.
Cheats live in the playbook as `script-execute` snippets; add code to the game only when the user agrees.

## 5. Replay

- Follow `matrix-shared-editor`: take the lock for each play session, release between.
- Drive the player through its own input path. Input System: queue the whole device state each time,
  `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, Key.LeftShift))` (one state holds
  every key that is down; send an empty `KeyboardState` to release), and put press and release in different
  frames. While you drive the game from outside the Editor window, set
  `InputSystem.settings.backgroundBehavior = BackgroundBehavior.IgnoreFocus` and
  `editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView`, and restore both afterwards. When the
  controller reads something else, call its move API.
- Teleport the way the body moves: a `CharacterController` must be disabled around the position change (or
  call `Physics.SyncTransforms()`), a `Rigidbody` uses `rb.position`, a `NavMeshAgent` uses `Warp`.
- UI: `pointerClickHandler` fits buttons and toggles; a line whose target only handles pointer down or drag
  needs `pointerDownHandler` / the drag events instead.
- Walking monitor: no progress (< 0.5 m in 2 s) with input held = **STUCK**; no ground under the
  player for 1 s or far below the recorded path = **FELL**. Record position + `screenshot-game-view`,
  try one sidestep or jump, then teleport to the next safe point and continue. Report it; never hide it.
- After every step run its check (`rql-query` or C#); on failure take a screenshot and the step's
  `console-get-logs`, then stop or continue as the playbook says.
- Report per step pass/fail, every STUCK/FELL with coordinates, console errors, total run time.

## Files

`MatrixWatch/<yyyyMMdd-HHmm>-<slug>/` at the project root (git-ignored): `summary.md`, `events.jsonl`,
`frames/`, `sheets/`, `input*.inputtrace` (Input System projects: raw events, one file per domain reload, in
order), and your `playbook.md`. Exact raw replay, last resort: `var trace = InputEventTrace.LoadFrom(path);
var replay = trace.Replay().WithAllDevicesMappedToNewInstances().PlayAllEventsAccordingToTimestamps();` keep
`replay` alive until `replay.finished`, then dispose both. It replays timing, not intent: it drifts as soon
as the game state differs.
";
    }
}
