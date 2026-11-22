#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using UnityEditor;

namespace Feeder.MCP.Editor
{
    // base for tools a project adds; the rules below are enforced when tools register, so a skill does not have to repeat them
    public abstract class MxProjectTool
    {
        public const string TitlePrefix = "Project / ";

        // runs on the main thread; a bad argument comes back to the agent as a caller error, not a crash log
        protected static T Run<T>(Func<T> body)
        {
            return MainThread.Instance.Run(() =>
            {
                try { return body(); }
                catch (ArgumentException e) { throw new ToolCallerException(e.Message, e); }
            });
        }

        // one Undo step; a failure halfway reverts what was done
        protected static void RunUndo(string name, Action body)
        {
            MainThread.Instance.Run(() =>
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(name);
                var group = Undo.GetCurrentGroup();
                try
                {
                    body();
                    Undo.CollapseUndoOperations(group);
                }
                catch (Exception e)
                {
                    Undo.RevertAllDownToGroup(group);
                    if (e is ArgumentException)
                        throw new ToolCallerException(e.Message, e);
                    throw;
                }
            });
        }

        #region Registration rules

        static readonly Dictionary<string, string> problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // registration problem per tool name, shown by unity-tool-list and the Skills window
        public static IReadOnlyDictionary<string, string> Problems => problems;

        public static bool IsProjectTool(MethodInfo method, string? title)
            => (title ?? string.Empty).StartsWith(TitlePrefix, StringComparison.Ordinal)
               || !(method.DeclaringType?.Assembly.GetName().Name ?? string.Empty).StartsWith("Feeder.", StringComparison.Ordinal);

        public static string? MissingDescription(MethodInfo method)
        {
            if (method.GetCustomAttribute<DescriptionAttribute>() == null)
                return $"[Description] on the method {method.DeclaringType?.Name}.{method.Name}";
            var bare = method.GetParameters()
                .Where(p => p.ParameterType != typeof(CancellationToken) && p.GetCustomAttribute<RequestIDAttribute>() == null)
                .FirstOrDefault(p => p.GetCustomAttribute<DescriptionAttribute>() == null);
            return bare == null ? null : $"[Description] on the parameter '{bare.Name}' of {method.DeclaringType?.Name}.{method.Name}";
        }

        // explicitChoice: the user's saved on/off for that tool, which always wins
        internal static void Enforce(IToolManager manager, Func<string, bool> explicitChoice)
        {
            problems.Clear();
            foreach (var tool in manager.GetAllTools().OfType<RunTool>())
            {
                var method = tool.Method;
                if (method == null || !IsProjectTool(method, tool.Title))
                    continue;

                var missing = MissingDescription(method);
                if (missing != null)
                {
                    problems[tool.Name] = $"[registration] '{tool.Name}' is missing {missing}. Agents pick tools by their descriptions; it stays off until one is added.";
                    UnityEngine.Debug.LogError("[Matrix Bridge] " + problems[tool.Name]);
                    manager.SetToolEnabled(tool.Name, false);
                    continue;
                }

                var declaredOn = method.GetCustomAttribute<AiToolAttribute>()?.Enabled == true;
                if (declaredOn && !explicitChoice(tool.Name) && manager.IsToolEnabled(tool.Name))
                {
                    UnityEngine.Debug.LogWarning($"[Matrix Bridge] Project tool '{tool.Name}' declares Enabled = true; project tools start off. " +
                        "It is treated as off: turn it on in the Skills window or with tool-set-enabled-state, and set Enabled = false in its attribute.");
                    manager.SetToolEnabled(tool.Name, false);
                }
            }
        }

        #endregion
    }
}
