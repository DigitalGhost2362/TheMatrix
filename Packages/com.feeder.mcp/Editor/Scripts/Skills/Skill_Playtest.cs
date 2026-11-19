#nullable enable
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiSkillType]
    public static class Skill_Playtest
    {
        public const string SkillId = "matrix-playtest";

        [AiSkill(SkillId,
@"Play-test the game with the 'playtest' tool, start to finish without asking: one call runs a flow from the project's Docs/Playtest.md in play mode, clicks uGUI buttons through a visible cursor, waits for panels to settle before shots, fails on console errors, puts PlayerPrefs and save files back, and answers with a step table and a contact sheet. Covers finding the UI, writing or fixing Playtest.md, keeping rewards and purchases out of flows, reading a result and changing the cursor. Use whenever the user asks for a playtest or play test, to test a UI flow, or to change the playtest cursor.")]
        public static string Markdown => @"
# Playtest

The runner is written once, in the package. A playtest is data: the steps of a flow live in the project's
`Docs/Playtest.md`, and one `playtest` call plays them. Never write a `script-execute` that clicks buttons
for a playtest; when a step cannot be expressed, say which verb is missing.

## Rules of the run

- The user asking for a playtest is the yes for every step of it: write or fix `Playtest.md`, enable the tool,
  enter play mode, run, fix the file and run again. Do not ask in between; report at the end.
- Each problem gets at most two fixes of the file (section 4); then report it as it is.
- Stop only for a real block: another person's session is in play mode (`matrix-shared-editor`), compile errors
  you did not cause, or a flow that cannot avoid destroying data that is not the agent's.

## 0. Tool and Editor

- `playtest` is off by default: `tool-set-enabled-state` with `{""Name"":""playtest"",""Enabled"":true}`, then call it
  through `tool-call` (`{""name"":""playtest"",""arguments"":{...}}`) until the client lists it.
- Follow `matrix-shared-editor`: `editor-lock-acquire` before the first run, `editor-lock-release` when done.
- The run enters play mode itself, starting in the file's `start-scene` (the open scenes stay as they are), and
  leaves play mode at the end unless `stay=true`. It refuses while the project has compile errors.
- `Tools > Feeder > Matrix Bridge > Getting Started` installs this skill with the others, and a package update that
  changes a skill text rewrites the skill files on the next Editor load.

## 1. Is the file there and current?

Call `playtest` with `action=list`. It prints the flows or the parse errors.

**Missing** (no `Docs/Playtest.md`, `docs/` or `ProjectDocs/`): write it before running anything.

**Out of date** when any of these holds; update the file first, then run what was asked:

- `list` reports parse errors, or the asked flow does not exist.
- The UI changed since the last `## Changelog` line of the file:
  `git log --since=<that date> --name-only -- Assets` touches `.prefab`, `.unity` or panel / popup / UI scripts
  that a flow goes through.
- A previous run failed with `not found`, `inactive`, `hidden` or a timeout on a step whose screen was redesigned.

### 1.1 Find the UI

Enter play mode in the start scene (or run a short flow with `stay=true`) and list every `Selectable` with its
path, label and whether a player can really see it:

```csharp
string Seg(Transform t)
{
    if (t.parent == null) return t.name;
    int same = 0, index = 0;
    foreach (Transform s in t.parent) { if (s.name != t.name) continue; if (s == t) index = same; same++; }
    return same > 1 ? $""{t.name}[{index}]"" : t.name;
}
var rows = new System.Collections.Generic.List<string>();
foreach (var s in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
{
    var go = s.gameObject;
    string state = null;
    var alpha = 1f;
    if (!go.activeInHierarchy) state = ""inactive"";
    for (var t = go.transform; t != null && state == null; t = t.parent)
    {
        var canvas = t.GetComponent<Canvas>();
        var group = t.GetComponent<CanvasGroup>();
        if (canvas != null && !canvas.enabled) state = ""hidden: Canvas off on "" + t.name;
        else if (group != null && group.enabled && (alpha *= group.alpha) <= 0.001f) state = ""hidden: alpha 0 on "" + t.name;
    }
    state ??= s.IsInteractable() ? ""shown"" : ""shown, not interactable"";
    var path = Seg(go.transform);
    for (var t = go.transform.parent; t != null; t = t.parent) path = Seg(t) + ""/"" + path;
    var label = """";
    foreach (var c in go.GetComponentsInChildren<Component>(true))
    {
        var p = c == null || c is UnityEngine.UI.Selectable ? null : c.GetType().GetProperty(""text"");
        if (p != null && p.PropertyType == typeof(string)) { label = (string)p.GetValue(c); break; }
    }
    rows.Add($""/{path} | \""{label}\"" | {state}"");
}
rows.Sort(StringComparer.Ordinal);
return string.Join(""\n"", rows);
```

Many UI frameworks hide a closed panel with `Canvas.enabled = false` and `CanvasGroup.alpha = 0` and keep its
GameObject **active**. There `expect-active` / `expect-inactive` on the panel root prove nothing: use
`expect-visible` / `expect-hidden`, or let the next `click` on the screen below prove the panel closed (it fails
with `covered by` while the panel is still up).

### 1.2 Keep real rewards out of the flows

In the Editor there is usually no ad SDK, so a rewarded or interstitial ad counts as watched at once and **pays
out for real**: free items, progress toward ""remove ads"", currency on returning home. Buying, opening a crate,
""quick join"" (a new world or room) and closing a shop while trying an item on also change saved data.

- Read the click handler of every button a flow would press. Leave out buttons that show an ad, buy, spend,
  create save data at random, or equip; write in the file why each is left out.
- Use fixed data: name one world / profile / slot in a `## Preparation` section (how it was made, its id), and
  reach it the same way every run, not through a random or ""newest"" choice when that changes.
- The runner snapshots PlayerPrefs (`HKCU\Software\Unity\UnityEditor\<company>\<product>` for play mode in the
  Windows Editor; a built player uses another key) and the files in `Application.persistentDataPath` before the run,
  lists what changed under `## Side effects` of the report, and puts both back once play mode has ended
  (`protect-prefs: on`, the default). With `stay=true` it lists but cannot restore. A save folder over 256 MB is
  listed but not restored: say so.

### 1.3 Write the flows

1. One flow per thing a player does (`open-settings`, `shop-tabs`, `enter-world`), each starting from the state
   the previous flow leaves or from a fresh start. A `full` flow chains them with `run`.
2. Target by hierarchy path (`/Canvas/Shop/List/Item[3]/BtnBuy`; `[n]` is the n-th sibling of that name, from
   0). Use the label in quotes (`""Buy""`) only when the path changes at runtime; a label prefers the button a
   player can see.
3. No `wait` before a click: a click already waits until its target is shown, interactable and on top. No `wait`
   before a `shot` either: a shot waits until the UI stops moving. For a game rule (a timer, a round end) use
   `wait-until` when the game has a static it can read; `wait <seconds>` only when it has none.
4. `expect-visible` / `expect-hidden` after the steps whose result matters; `shot` on every new screen.
5. `## Clear screen` hides what lies over the game and is not under test: debug strips, FPS counters, test ad
   banners, console overlays. A popup players also get (daily reward, tutorial hand) is part of the game: close it
   with a click.
6. The run folder must stay out of git: `git check-ignore -v Docs/Playtest_Runs/x`. Not ignored: add
   `Docs/Playtest_Runs/` to `.gitignore`, or to `.git/info/exclude` when `.gitignore` cannot change.
7. Append one line to `## Changelog`: date, what changed, why.

### File format

```md
# Playtest - <game>

Notes for whoever writes flows here: how panels hide, which buttons are left out and why.

## Preparation
- The world ""Playtest Creative"" (made 2026-01-01 with WorldLibrary.Create(...)) must exist.

## Setup
start-scene: Assets/Scenes/Boot.unity    # play mode starts here
speed: 2                                 # cursor speed; 1 = calm demo, 4 = fast
timeout: 5                               # seconds a step waits for its target
cursor: on                               # off = fastest, no cursor
autoshot: clicks                         # every click adds a row to the contact sheet: before, over, press, result
shot-settle: 2                           # seconds a shot may wait for the UI to stop moving; 0 = two frames only
fail-on-console-errors: true             # a console error fails the run
protect-prefs: on                        # snapshot and restore PlayerPrefs and persistentDataPath
cursor-theme: Assets/Editor/MyCursor.asset
cursor-layers: sprite, outline, caption, blocked
maximize: false

## Clear screen
- hide /DebugCanvas
- hide-type FpsCounter

## Flow: open-settings
What it proves, in one line.
- click /Canvas/Home/BtnSettings
- expect-visible /Canvas/SettingsPanel/Window
- shot settings
- click /Canvas/SettingsPanel/Window/BtnClose
- expect-hidden /Canvas/SettingsPanel/Window

## Flow: full
- run open-settings

## Known issues

## Changelog
- 2026-01-01 first version
```

| Verb | Does |
|---|---|
| `click <path or ""label""> [timeout]` | waits until shown, interactable and not covered, moves the cursor, clicks (enter, down, up, click) |
| `expect-visible <path> [timeout]` / `expect-hidden` | shown = active, every Canvas above it on, alpha above 0; fails on timeout |
| `expect-active <path> [timeout]` / `expect-inactive` | GameObject active state only |
| `wait <seconds>` | real time |
| `wait-until <Type.Member> <value> [timeout]` | until a static field, property or parameterless method reads `<value>` (invariant text, any case); fails with `still <value>` on timeout |
| `hide <path>` / `hide-type <Component>` | deactivates for this run; `absent` when not there |
| `shot <name> [settle seconds]` | Game view PNG with the cursor, once tweens, fades and panel moves have stopped; saved as `<step>_<name>.png` |
| `call <Type.Method> [args]` | a static method: cheats and setup that are not under test |
| `timescale <n>` | `Time.timeScale`, restored after the run |
| `run <flow>` | inlines another flow |

Comments start with ` #`. Steps are lines starting with `- ` under a `## Clear screen` or `## Flow:` heading;
other text is free. Clear screen runs before every flow.

## 2. Run

`playtest` with `action=run`, `flow=<name>`. Optional: `speed` (0 = the file's), `cursor=false` (fastest, when
nobody watches), `stay=true` (keep play mode; saves are then listed, not restored), `file` (another playtest file).
The answer arrives after play mode has ended and the saves are restored. `action=status` shows the live step;
`action=stop` ends a run.

## 3. Read the result

The answer: a verdict line, one row per step (`ok`, `FAIL`, `absent`, `skip`, time, note), the console errors,
`## Side effects`, the contact sheet path and the report path (`Docs/Playtest_Runs/<stamp>-<flow>/`).

- **PASS needs zero console errors.** `FAIL (console errors: n)` with every step ok is a game bug: report the step
  and the first error lines. Turn `fail-on-console-errors` off only when the user says those errors are known.
- Side effects: any line there is a change the flow made to saved data. It was restored, but the flow should not
  make it: find the button that caused it and take it out (1.2), unless the flow is about that change.
- Show the contact sheet in your answer with the `![](<absolute path>/contact_sheet.png)` line the report gives,
  and look at it: each shot must show its screen finished, not halfway through opening. A note `still moving
  after 2 s` on a shot means something keeps animating: raise that shot's settle seconds, or accept it when the
  motion is an endless loop.

On FAIL decide what it is:

| Note | Usually | Do |
|---|---|---|
| `not found` | path renamed or moved | find the new path (1.1), fix the flow, Changelog line, run again |
| `covered by <path>` | a debug overlay, or a real popup | overlay: add it to Clear screen; popup players see: add a click that closes it |
| `hidden: Canvas off` / `hidden: alpha 0` | the panel did not open, or opens later | read the handler: slower by design -> raise that step's timeout; never opens -> game bug |
| `inactive` / timeout | as above, for panels that deactivate | as above |
| `not interactable` | a condition is not met | add the step that meets it (toggle, data in Preparation), or report |
| console errors | game bug | report the step, the first error lines and the shot |

Never change a flow so a real bug passes, and never change game code to make a flow pass unless the user asks.

## 4. Report

One line PASS/FAIL with steps, seconds and console errors; the failed step and why; the side effects; what
changed in Playtest.md; the contact sheet shown inline; the report path. Release the editor lock.

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
builds. Shots and contact sheet frames get the sprite and the target outline painted in from the cursor state;
custom layers are not painted into shots.
";
    }
}
