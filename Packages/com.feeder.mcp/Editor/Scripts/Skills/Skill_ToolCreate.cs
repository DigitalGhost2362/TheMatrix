#nullable enable
using Feeder.ToolHost;

namespace Feeder.MCP.Editor.API
{
    [AiSkillType]
    public static class Skill_ToolCreate
    {
        public const string SkillId = "matrix-tool-create";

        [AiSkill(SkillId,
@"Add a project-specific Matrix tool: a C# file in an Editor folder that Unity compiles into a new MCP tool. Use when the same script-execute job came up a third time in this project and no tool fits, or when the user asks for a custom Matrix tool.")]
        public static string Markdown => @"
# Create a project tool

## Use when

- The same `script-execute` job came up three times or more in this project, or the user asks for a tool. A one-off job stays a script.
- `unity-tool-list` with a regex found nothing that fits; a disabled tool counts, turn it on instead.

## Steps

1. One tool per area of the project, with an `action` parameter for its jobs. Add an action to that area's tool before writing a second tool.
2. Put the file in an Editor folder, for example `Assets/Editor/MatrixTools/Tool_Level.cs`. With asmdefs, use an Editor asmdef that references `Feeder.MCP.Editor`, `Feeder.ToolHost` and `Feeder.Reflect`.
3. Write it with your file tools, `assets-refresh`, then read the console until it compiles clean. Registration problems show there and in `unity-tool-list`.
4. Turn it on with `tool-set-enabled-state` and call it through `tool-call`; a client restart lists it directly.
5. Tell the user which tool and actions you added.

## Sample

Derive from `MxProjectTool`; check input with `MxGuard`; reuse `Mx` helpers. Descriptions stay short: what it does and the actions.

```csharp
using System.ComponentModel;
using Feeder.MCP.Editor;
using Feeder.ToolHost;

[AiToolType]
public class Tool_Level : MxProjectTool
{
    static readonly string[] Actions = { ""reset"", ""shot"" };

    [AiTool(""project-level"", Title = ""Project / Level"", Enabled = false)]
    [Description(""Level debug actions: reset | shot."")]
    public string Level([Description(""reset | shot"")] string action, [Description(""Level number, 1 to 50."")] int level = 1)
    {
        action = MxGuard.OneOf(action, Actions, nameof(action));
        MxGuard.InRange(level, 1, 50, nameof(level));
        return Run(() => action == ""reset""
            ? $""{Mx.Call(typeof(YourDebugMenu), ""ResetLevel"", level)}""
            : Mx.Shot($""Temp/level-{level}.png""));
    }
}
```

Scene or asset edits go inside `RunUndo(""Name"", () => ...)`. Work that crosses a domain reload (scripts, play mode, packages) belongs to the existing tools, not to a project tool.
";
    }
}
