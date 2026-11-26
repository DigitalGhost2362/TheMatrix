#nullable enable
using System;
using System.ComponentModel;
using Feeder.MCP.Editor.Playtest;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Playtest
    {
        public const string PlaytestToolId = "playtest";

        [AiTool
        (
            PlaytestToolId,
            Title = "Editor / Playtest",
            Enabled = false
        )]
        [AiSkillDescription("Run a flow of the project's Docs/Playtest.md in play mode with one call: the runner hides what covers the screen, clicks uGUI buttons through a visible cursor as soon as each is reachable, checks panels, takes settled shots, fails on console errors, restores PlayerPrefs and save files, and answers with a step table and a contact sheet.")]
        [AiSkillBody(SkillBody)]
        [Description("Run a playtest flow from the project's Playtest.md. action = run | status | stop | list. " +
            "run enters play mode when needed and answers after play mode ends, with a step table, the save side effects, a contact sheet and a report path.")]
        public ResponseCallTool Run
        (
            [Description("run | status | stop | list")]
            string action = "status",
            [Description("run: the flow name, the text after '## Flow:'.")]
            string? flow = null,
            [Description("Playtest file relative to the project root. Empty = Docs/Playtest.md (or docs/, ProjectDocs/).")]
            string? file = null,
            [Description("run: cursor and move speed multiplier. 0 = the file's 'speed:' or 1.")]
            float speed = 0f,
            [Description("run: false clicks without the cursor. Empty = the file's 'cursor:' or on.")]
            bool? cursor = null,
            [Description("run: true keeps play mode on after the flow when the run entered it.")]
            bool stay = false,
            [Description("run: true plays the flow on the linked device build (Matrix Device) instead of the Editor's play mode.")]
            bool device = false,
            [RequestID]
            string? requestId = null
        )
        {
            return MainThread.Instance.Run(() =>
            {
                switch ((action ?? "status").Trim().ToLowerInvariant())
                {
                    case "run":
                        if (string.IsNullOrWhiteSpace(flow))
                            return ResponseCallTool.Error("run needs 'flow'. Call action=list to see the flows.");
                        try
                        {
                            return PlaytestRunner.Start(file, flow!.Trim(), speed, cursor, stay, device, requestId, out var message)
                                ? ResponseCallTool.Processing(message).SetRequestID(requestId!)
                                : ResponseCallTool.Success(message + " Call action=status for the result.");
                        }
                        catch (Exception e)
                        {
                            return ResponseCallTool.Error(e.Message);
                        }
                    case "status":
                        return ResponseCallTool.Success(PlaytestRunner.Status());
                    case "stop":
                        return ResponseCallTool.Success(PlaytestRunner.Stop());
                    case "list":
                        try { return ResponseCallTool.Success(PlaytestRunner.LoadFile(file).Describe()); }
                        catch (Exception e) { return ResponseCallTool.Error(e.Message); }
                    default:
                        return ResponseCallTool.Error($"Unknown action '{action}'. Use run, status, stop or list.");
                }
            });
        }

        const string SkillBody = @"Runs one flow of the project's Docs/Playtest.md in play mode and answers with a step table.

- `list`: flows of the file, or its parse errors.
- `run` + `flow`: plays it; `speed`, `cursor=false`, `stay=true`, `file` are optional. The answer comes after play mode ends and saves are restored; any console error fails the run; show the contact sheet line it gives.
- `status`: the live step, or the last report. `stop`: ends a run.
- `device=true`: plays the flow on a phone running a device build (Tools > Feeder > Matrix Bridge > Device, or `build-start device=true`); same report and contact sheet, shots taken on the phone, no save restore.

File format, verbs, reading failures and changing the cursor: skill `" + Skill_Playtest.SkillId + "`.";
    }
}
