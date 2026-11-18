#nullable enable
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiSkillType]
    public static class Skill_Playtest
    {
        public const string SkillId = "matrix-playtest";

        [AiSkill(SkillId,
@"Play-test the game with the 'playtest' tool: one call runs a flow from the project's Docs/Playtest.md in play mode, clicks uGUI buttons through a visible cursor as soon as each one shows, and answers with a step table. Covers writing Playtest.md when it is missing or out of date, running and reading a flow, fixing the file versus reporting a game bug, and changing the cursor. Use whenever the user asks for a playtest or play test, to test a UI flow, or to change the playtest cursor.")]
        public static string Markdown => @"
# Playtest

The runner is written once, in the package. A playtest is data: the steps of a flow live in the project's
`Docs/Playtest.md`, and one `playtest` call plays them. Never write a `script-execute` that clicks buttons
for a playtest; when a step cannot be expressed, say which verb is missing.

## 0. Tool and Editor

- `playtest` is off by default: `tool-set-enabled-state` with `playtest` on, then call it through `tool-call`
  until the client lists it.
- Follow `matrix-shared-editor`: `editor-lock-acquire` before the first run, `editor-lock-release` when done.
- The run enters play mode itself, starting in the file's `start-scene` (the open scenes stay as they are),
  and leaves play mode at the end unless `stay=true`. It refuses while the project has compile errors.

## 1. Is the file there and current?

Call `playtest` with `action=list`. It prints the flows or the parse errors.

**Missing** (no `Docs/Playtest.md`, `docs/` or `ProjectDocs/`): write it before running anything.

**Out of date** when any of these holds; update the file first, then run what was asked:

- `list` reports parse errors, or the asked flow does not exist.
- The UI changed since the last `## Changelog` line of the file:
  `git log --since=<that date> --name-only -- Assets` touches `.prefab`, `.unity` or panel / popup / UI scripts
  that a flow goes through.
- The user says the screens changed, or a previous run failed with `not found`, `inactive` or a timeout on a
  step whose screen was redesigned.

### Writing or updating it

1. Find the screens: the start scene (the first enabled scene of the build settings, or the one the user
   names), the Canvas roots, and every `Button` / `Toggle` on the way. `rql-query` lists them with their path
   and label without opening anything; read the click handlers to learn what each button opens or unlocks.
2. One flow per thing a player does (`open-settings`, `buy-item`, `claim-daily`), each starting from the state
   the previous flow leaves or from a fresh start. A `full` flow chains them with `run`.
3. Target by hierarchy path (`/Canvas/Shop/List/Item[3]/BtnBuy`; `[n]` is the n-th sibling of that name,
   from 0). Use the button label in quotes (`""Buy""`) only when the path changes at runtime.
4. Add no `wait` before a click: a click already waits until its target is active, interactable and on top.
   Use `wait` only for a game rule (a timer that must expire).
5. Add `expect-active` / `expect-inactive` after the steps whose result matters, and `shot` where the
   user wants to see the screen.
6. `## Clear screen` hides what lies over the game and is not under test: debug strips, FPS counters, ads test
   banners, console overlays. A popup that players also get (daily reward, tutorial hand) is part of the game:
   close it with a click step instead of hiding it.
7. New file: add `<docs folder>/Playtest_Runs/` to the project's `.gitignore` (shots and reports of each run).
8. Append one line to `## Changelog`: date, what changed, why. Ask the user only what the code cannot tell
   (which item to buy, which account state to start from).

### File format

```md
# Playtest - <game>

## Setup
start-scene: Assets/Scenes/Boot.unity    # play mode starts here
speed: 2                                 # cursor speed; 1 = calm demo, 4 = fast
timeout: 5                               # seconds a step waits for its target
cursor: on                               # off = fastest, no cursor
cursor-theme: Assets/Editor/MyCursor.asset
cursor-layers: sprite, outline, caption, blocked
maximize: false

## Clear screen
- hide /DebugCanvas
- hide-type FpsCounter

## Flow: open-settings
What it proves, in one line.
- click /Canvas/Home/BtnSettings
- click ""Sound""
- expect-active /Canvas/SettingsPanel
- shot settings
- click /Canvas/SettingsPanel/BtnClose
- expect-inactive /Canvas/SettingsPanel

## Flow: full
- run open-settings

## Known issues

## Changelog
- 2026-01-01 first version
```

| Verb | Does |
|---|---|
| `click <path or ""label""> [timeout]` | waits until visible, interactable and not covered, moves the cursor, clicks (enter, down, up, click) |
| `expect-active <path> [timeout]` / `expect-inactive` | waits for the state, fails on timeout |
| `wait <seconds>` | real time |
| `hide <path>` / `hide-type <Component>` | deactivates for this run; `absent` when not there |
| `shot <name>` | Game view PNG into the run folder, two frames after the previous step |
| `call <Type.Method> [args]` | a static method: cheats and setup that are not under test |
| `timescale <n>` | `Time.timeScale`, restored after the run |
| `run <flow>` | inlines another flow |

Comments start with ` #`. Steps are lines starting with `- ` under a `## Clear screen` or `## Flow:` heading;
other text is free. Clear screen runs before every flow.

## 2. Run

`playtest` with `action=run`, `flow=<name>`. Optional: `speed` (0 = the file's), `cursor=false` (fastest, when
nobody watches), `stay=true` (keep play mode so the user sees the end state), `file` (another playtest file).
The answer arrives when the flow ends. `action=status` shows the live step or the last report; `action=stop`
ends a run.

While the cursor is on it stays on the Game view, resting where it last clicked, until play mode ends.

## 3. Read the result

The answer is a table: one row per step with `ok`, `FAIL`, `absent` or `skip`, its time and a note, then the
console errors of the run and the report path (`Docs/Playtest_Runs/<stamp>-<flow>/report.md`, with the shots).
Open a shot only for a failed step or when the user asked to see a screen.

On FAIL decide what it is:

| Note | Usually | Do |
|---|---|---|
| `not found` | path renamed or moved | find the new path, fix the flow, Changelog line, run again |
| `covered by <path>` | a debug overlay, or a real popup | overlay: add it to Clear screen; popup players see: add a click that closes it |
| `inactive` / timeout | the panel opens later now, or not at all | read the handler: slower by design -> raise that step's timeout; never opens -> game bug |
| `not interactable` | a condition is not met | add the step that meets it (toggle, purchase), or report |
| console errors | game bug | report the step, the first error lines and the shot |

Fix the file at most twice per request; then stop and report. Never change a flow so a real bug passes, and
never change game code to make a flow pass unless the user asks.

## 4. Report

One line PASS/FAIL with steps and seconds, the failed step and why, what changed in Playtest.md, the
report path. Release the editor lock.

## Faster play mode

Entering play mode reloads every script unless `Enter Play Mode Options > Disable Domain Reload` is on; the
runner works either way. Turning it on is a project decision: see `matrix-setup-checklist`. With it on, a bug
that only appears from the second run on is most likely a static field that is not reset.

## Changing the cursor

The default cursor is the Matrix glitch arrow. A project changes it without touching the package:

1. Sprites: one idle image, and one click sheet with the frames laid out left to right, top to bottom on a grid
   (default 5 x 4 = 20 frames at 30 fps, 256 px per frame). The pointer tip sits at the same spot in every frame,
   and the last frame should match the idle image so the return is seamless.
2. Import both as `Default` textures, no mipmaps, `Alpha Is Transparency` on.
3. `Create > Matrix > Playtest Cursor Theme`, under an Editor folder. Fill `idle`, `clickSheet`, `columns`,
   `rows`, `frames`, `fps`, `hitFrame` (the frame on which the click reaches the game), `size` (drawn size of
   a frame, Game view points), `hotspot` (where the tip is inside a frame, 0..1 from the top-left), move
   timings and colors.
4. Point the file at it: `cursor-theme: Assets/Editor/PlaytestCursor.asset` in `## Setup`.
5. Run any short flow with `stay=true` and look at the Game view.

Layers: `sprite`, `outline` (box around the target), `caption` (step text), `blocked` (red note when something
covers the target). Leave some out with `cursor-layers:` in the file or `disabledLayers` on the theme.
A new layer is a class in an Editor folder:

```csharp
using Feeder.MCP.Editor.Playtest;
using UnityEngine.UIElements;

public sealed class TrailCursorLayer : IPlaytestCursorLayer
{
    public string Id => ""trail"";
    public int Order => 50;   // higher draws on top; the sprite is 100

    public VisualElement Create(PlaytestCursorTheme theme) => new VisualElement();

    public void Refresh(VisualElement element, PlaytestCursorState state, PlaytestViewMap map, PlaytestCursorTheme theme)
    {
        var tip = map.Point(state.Position);   // state is 0..1 of the game screen; map turns it into points
        element.style.left = tip.x;
        element.style.top = tip.y;
    }
}
```

Keep `Refresh` cheap: it runs every frame of a run and twice a second at rest. UI Toolkit skips a style
set to its current value, so a cursor at rest does not repaint the Game view.
The cursor is UI Toolkit drawn over the Game view window: nothing is added to the scene, the game, raycasts or
builds, and Game view captures do not contain it.
";
    }
}
