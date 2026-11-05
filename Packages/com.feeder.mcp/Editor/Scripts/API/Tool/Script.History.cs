#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    // Last runs of script-execute, kept in SessionState so they survive a domain reload. Main thread only.
    internal static class ScriptRunHistory
    {
        const string Key = "Feeder_MCP_ScriptRunHistory";
        const int MaxEntries = 20;
        internal const int MaxCodeChars = 20000;

        [Serializable]
        public sealed class Entry
        {
            public int id;
            public string when = string.Empty;
            public string code = string.Empty;
            public string className = string.Empty;
            public string methodName = string.Empty;
            public bool isMethodBody;
            public string parametersJson = string.Empty;
            public bool tooLarge;
        }

        [Serializable]
        sealed class Store
        {
            public int nextId = 1;
            public List<Entry> entries = new List<Entry>();
        }

        static Store Load()
        {
            var json = SessionState.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new Store();
            try { return JsonUtility.FromJson<Store>(json) ?? new Store(); }
            catch (Exception) { return new Store(); }
        }

        public static int Add(string code, string className, string methodName, bool isMethodBody, string parametersJson)
        {
            var store = Load();
            var tooLarge = code.Length > MaxCodeChars;
            var entry = new Entry
            {
                id = store.nextId++,
                when = DateTime.Now.ToString("HH:mm:ss"),
                code = tooLarge ? string.Empty : code,
                className = className,
                methodName = methodName,
                isMethodBody = isMethodBody,
                parametersJson = parametersJson,
                tooLarge = tooLarge
            };
            store.entries.Add(entry);
            if (store.entries.Count > MaxEntries)
                store.entries.RemoveRange(0, store.entries.Count - MaxEntries);
            SessionState.SetString(Key, JsonUtility.ToJson(store));
            return entry.id;
        }

        public static Entry? Get(int id) => Load().entries.FirstOrDefault(e => e.id == id);

        public static List<Entry> Recent(int limit)
            => Load().entries.AsEnumerable().Reverse().Take(Math.Max(1, limit)).ToList();
    }

    public static partial class Tool_Script
    {
        public const string ScriptExecuteHistoryToolId = "script-execute-history";
        [AiTool
        (
            ScriptExecuteHistoryToolId,
            Title = "Script / Execute History",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("List the most recent '" + ScriptExecuteToolId + "' runs (up to 20, kept until the Editor closes, " +
            "across domain reloads). Pass an id as `replayId` to '" + ScriptExecuteToolId + "' to run it again.")]
        [AiSkillBody("Returns one line per run, newest first: `id`, time, class.method, mode and the first line of the code. " +
            "`includeCode=true` prints the full code. Runs that failed to compile are not recorded.")]
        [Description("Lists recent script-execute runs with their ids. Use an id as 'replayId' in script-execute to repeat a run.")]
        public static string ExecuteHistory
        (
            [Description("How many runs to list, newest first.")]
            int limit = 10,
            [Description("Print the full code of each run, not just its first line.")]
            bool includeCode = false
        )
        {
            var entries = MainThread.Instance.Run(() => ScriptRunHistory.Recent(limit));
            if (entries.Count == 0)
                return "No script-execute runs recorded in this Editor session.";

            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                var code = e.tooLarge ? "(code too large to keep; cannot be replayed)" : e.code;
                var first = code.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
                sb.AppendLine($"#{e.id} {e.when} {e.className}.{e.methodName} {(e.isMethodBody ? "body" : "full")}: {(includeCode ? string.Empty : first)}");
                if (includeCode)
                    sb.AppendLine(code);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
