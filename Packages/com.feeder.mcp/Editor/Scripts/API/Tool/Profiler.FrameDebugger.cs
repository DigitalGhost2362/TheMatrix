#nullable enable
using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Profiler
    {
        public const string FrameDebuggerToolId = "profiler-frame-debugger";

        // the Frame Debugger API is internal and its shape changes between Unity versions, so it is reached by name
        static readonly Type? FrameDebuggerType = typeof(UnityEditor.Editor).Assembly.GetTypes().FirstOrDefault(t => t.Name == "FrameDebuggerUtility");
        // every native method reached by reflection, each checked against the 6000.4 metadata: SetEnabled(bool,int), GetRemotePlayerGUID(), GetFrameEvents(), GetFrameEventInfoName(int).
        // GetFrameEventData is deliberately absent: it needs a pre-built native event-data object and crashes the Editor when handed anything else.
        internal static readonly string[] ReflectedFrameDebuggerMethods = { "SetEnabled", "GetRemotePlayerGUID", "GetFrameEvents", "GetFrameEventInfoName" };

        [AiTool(FrameDebuggerToolId, Title = "Profiler / Frame debugger", Enabled = false)]
        [AiSkillDescription("Capture the rendering events (draw calls, clears, blits) of the current frame with the Frame Debugger: action=enable, events (index, type and name), disable, status.")]
        [AiSkillBody("# Profiler / Frame debugger\n\nTypical loop: `action=enable` (freezes the Game View on the current frame, a Game View window must be open), wait a moment for the Editor to repaint (the Editor window must have focus, otherwise nothing is captured: check `status` and enable again if the count stays 0), `action=events` to list every event with its index, type and name (`detail=true` is not available and answers `[unsupported]`: the per-event data call is native and unsafe to reach from here), then `action=disable` so the Game View runs again. `events` accepts `from` and `count` to page through large frames. `status` reports whether it is enabled and the event count. While enabled the Game View does not update, so always disable it when done.")]
        [Description("Frame Debugger: action=enable | events (from, count) | disable | status. Disable it when finished.")]
        public ResponseCallTool FrameDebugger
        (
            [Description("enable | events | disable | status")]
            string action = "status",
            [Description("events: first event index.")]
            int from = 0,
            [Description("events: max events to list (1-200).")]
            int count = 50,
            [Description("Not available: answers [unsupported] (per-event native data is unsafe to read from here).")]
            bool detail = false
        )
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    var type = FrameDebuggerType ?? throw new NotSupportedException("this Unity version has no FrameDebuggerUtility");
                    switch (action)
                    {
                        case "enable":
                            Call(type, "SetEnabled", true, Call(type, "GetRemotePlayerGUID"));
                            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                            return ResponseCallTool.Success("frame debugger enabled; call action=events after the Editor repainted (needs an open Game View)");
                        case "disable":
                            Call(type, "SetEnabled", false, Call(type, "GetRemotePlayerGUID"));
                            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                            return ResponseCallTool.Success("frame debugger disabled");
                        case "status":
                            return ResponseCallTool.Success($"enabled={IsOn(type)} events={Get(type, "count")} limit={Get(type, "limit")}");
                        case "events":
                            return Events(type, from, Mathf.Clamp(count, 1, 200), detail);
                        default:
                            return ResponseCallTool.Error($"unknown action '{action}'. Valid: enable|events|disable|status");
                    }
                }
                catch (NotSupportedException ex) { return ResponseCallTool.Error("[unsupported] " + ex.Message); }
                catch (Exception ex) { return ResponseCallTool.Error(ex.InnerException?.Message ?? ex.Message); }
            });
        }

        static ResponseCallTool Events(Type type, int from, int count, bool detail)
        {
            if (detail)
                return ResponseCallTool.Error("[unsupported] per-event detail is not available on this Unity version");
            if (!IsOn(type))
                return ResponseCallTool.Error("[not_enabled] the frame debugger is off; call action=enable first");
            var total = Get(type, "count") is int n ? n : 0;
            if (total == 0)
                return ResponseCallTool.Error("[no_events] no events captured yet; open a Game View, repaint the Editor and retry");
            var events = Call(type, "GetFrameEvents") as Array;
            var sb = new StringBuilder();
            sb.Append("events ").Append(total).AppendLine();
            var end = Math.Min(total, from + count);
            for (var i = Math.Max(0, from); i < end; i++)
            {
                sb.Append('#').Append(i).Append(' ');
                var ev = events != null && i < events.Length ? events.GetValue(i) : null;
                var kind = ev == null ? null : FieldText(ev, "type") ?? FieldText(ev, "m_Type");
                if (kind != null) sb.Append(kind).Append(' ');
                sb.Append(Call(type, "GetFrameEventInfoName", i) as string);
                sb.AppendLine();
            }
            if (end < total) sb.Append("... next from=").Append(end);
            return ResponseCallTool.Success(sb.ToString().TrimEnd());
        }

        static string? FieldText(object target, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = target.GetType();
            var value = (t.GetField(name, flags) ?? t.GetField("m_" + name, flags))?.GetValue(target) ?? t.GetProperty(name, flags)?.GetValue(target);
            return value?.ToString();
        }

        const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static object? Call(Type type, string method, params object?[] args)
        {
            if (!ReflectedFrameDebuggerMethods.Contains(method))
                throw new NotSupportedException($"FrameDebuggerUtility.{method} is not on the verified call list");
            var m = type.GetMethods(StaticFlags).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length)
                    ?? throw new NotSupportedException($"FrameDebuggerUtility.{method} is not available in this Unity version");
            return m.Invoke(null, args);
        }

        // newer Unity versions expose no enabled flag; a captured event count means the debugger is active
        static bool IsOn(Type type) => Get(type, "enabled") is bool on ? on : Get(type, "count") is int n && n > 0;

        static object? Get(Type type, string name)
        {
            try { return type.GetProperty(name, StaticFlags)?.GetValue(null) ?? type.GetField(name, StaticFlags)?.GetValue(null); }
            catch (Exception) { return null; }
        }
    }
}
