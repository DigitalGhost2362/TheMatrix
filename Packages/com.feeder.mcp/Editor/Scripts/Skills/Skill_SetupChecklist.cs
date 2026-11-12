#nullable enable
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiSkillType]
    public static class Skill_SetupChecklist
    {
        public const string SkillId = "matrix-setup-checklist";

        [AiSkill(SkillId,
@"Steps after Tools > Feeder > Matrix Bridge > Setup that need a judgment about the project or admin rights: turning off domain reload on entering play mode (check the project's static state first and leave a note for later agents) and a Windows Defender exclusion for the project folder. Use when the user asks to finish or tune the Matrix setup on a machine or project.")]
        public static string Markdown => @"
# Matrix Setup Checklist

`Tools > Feeder > Matrix Bridge > Getting Started` already turned Matrix on and set Auto Refresh off,
Script Changes While Playing to Recompile After Finished Playing and
Run In Background on (Revert in Getting Started undoes them). The steps below are left to you and the
user because they depend on the project or need admin rights. Ask before each one; never do them silently.

## 1. Enter play mode without a domain reload

Entering play mode reloads every script assembly, which takes from a few seconds to tens of seconds.
With domain reload off it takes under a second, which matters when agents play-test often. The cost:
static fields, static events and singletons keep their values from the last play session.

### Check the project first

Search the project's own code (`Assets/` and embedded packages under `Packages/`; skip
`Library/PackageCache`) for static state that a play session changes:

- static fields that are not `const` or `readonly` (counters, caches, lists, flags), including `static T Instance` singletons
- static events and delegates (`static event`, `static Action`): handlers pile up across sessions
- `DontDestroyOnLoad` managers that keep a static reference to themselves
- code that relies on static constructors running once per play session

Each one is safe only when one of these holds:

- it is reset in a method with `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
- it holds data that stays valid across play sessions (an immutable lookup table, a cached asset path)
- it is only written in edit mode

Report the list to the user with a verdict per item (safe / needs a reset / unsure). Third-party plugins
are a common source of trouble; name the ones you could not check.

### Turn it on, only with the user's yes

Through `script-execute` (needs the editor lock):

```csharp
EditorSettings.enterPlayModeOptionsEnabled = true;
EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
```

Scene reload stays on. This writes `ProjectSettings/EditorSettings.asset`, so it reaches the whole team
on commit: say so. To undo, set `enterPlayModeOptionsEnabled = false`.

### Leave a note for later agents

Add this to the project's `CLAUDE.md` (or `AGENTS.md`, whichever the project uses), with the real list:

```md
## Play mode: domain reload is off

Entering play mode does not reset static state. Any new static field, static event or singleton must be
reset in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` method.
Statics already checked: <list>. A bug that only shows up on the second play session is most likely a
static that is not reset.
```

## 2. Windows Defender exclusion

Real-time scanning reads every file Unity imports and compiles, so a scan-free project folder imports
and compiles faster. The trade-off: files in that folder are no longer scanned, so only exclude projects
whose sources you trust.

This needs an elevated PowerShell. Do not try to elevate yourself; give the user the command:

```powershell
Add-MpPreference -ExclusionPath ""<project root>""
```

`Library` is where most of the reads happen; exclude only `<project root>\Library` if the user wants a
narrower rule. To undo: `Remove-MpPreference -ExclusionPath ""<same path>""`. Neither step is undone
by Revert in Getting Started.
";
    }
}
