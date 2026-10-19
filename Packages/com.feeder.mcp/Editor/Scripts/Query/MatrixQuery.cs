#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    public sealed class QueryResult
    {
        public string Text = string.Empty;
        public int Total;
        public IReadOnlyList<object?> Items = Array.Empty<object?>();
        public IReadOnlyList<string> Warnings = Array.Empty<string>();
        public IReadOnlyList<Receipt> Receipts = Array.Empty<Receipt>();
    }

    // entry point of the query language: Run reads, Mutate changes the project in one undo step
    public static class MatrixQuery
    {
        public const string UndoName = "Matrix query";
        const int CacheSize = 128;
        const int MaxReceiptRows = 200;

        static readonly Dictionary<string, ParsedQuery> cache = new Dictionary<string, ParsedQuery>();
        static readonly LinkedList<string> cacheOrder = new LinkedList<string>();

        #region Public

        public static QueryResult Run(string query, int limit = 100, string format = "table")
        {
            if (limit < 1)
                throw new QueryException("limit must be at least 1");
            var parsed = Parse(query);
            if (parsed.HasMutation || parsed.CreatesObjects)
            {
                var stage = parsed.Stages.FirstOrDefault(s => s.IsMutation);
                throw new QueryException($"'{stage?.Keyword ?? "new"}' changes the project; run this query with rql-mutate", stage?.Position ?? 0);
            }
            var ctx = new QueryContext(null);
            var items = Execute(parsed, ctx, query);
            var text = format.Equals("json", StringComparison.OrdinalIgnoreCase)
                ? QueryFormat.Json(items, limit, items.Count)
                : format.Equals("table", StringComparison.OrdinalIgnoreCase)
                    ? QueryFormat.Table(items, limit, items.Count)
                    : throw new QueryException($"Unknown format '{format}'. Use table or json.");
            return new QueryResult { Text = AppendWarnings(text, ctx.Warnings), Total = items.Count, Items = items, Warnings = ctx.Warnings };
        }

        public static QueryResult Mutate(string query, bool dryRun = false, int maxTargets = 100, bool save = false)
        {
            if (maxTargets < 1)
                throw new QueryException("maxTargets must be at least 1");
            var parsed = Parse(query);
            if (!parsed.HasMutation && !parsed.CreatesObjects)
                throw new QueryException("This query changes nothing. Add a stage such as '| set name = \"X\"', '| call SetActive(false)', '| add Rigidbody' or '| destroy', or use rql-query to read.");
            if (save && dryRun)
                throw new QueryException("save and dryRun cannot be combined");
            var session = new MutationSession(dryRun, maxTargets);
            var ctx = new QueryContext(session);

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            List<object?> items;
            try
            {
                items = Execute(parsed, ctx, query);
            }
            catch (Exception e)
            {
                var applied = session.Receipts.Count > 0;
                Undo.RevertAllDownToGroup(group);
                if (!applied || e is not QueryException queryError)
                    throw;
                throw new QueryException(queryError.Message + "\nEarlier stages had already run; all of their changes were rolled back.", queryError.Position);
            }
            Undo.CollapseUndoOperations(group);
            if (dryRun)
                Undo.RevertAllDownToGroup(group);

            var saved = save ? Save(session, ctx) : null;
            var text = new StringBuilder();
            text.Append(Summary(session, dryRun, saved));
            if (session.Receipts.Count > 0)
            {
                text.Append("\ntarget | action | old | new");
                foreach (var r in session.Receipts.Take(MaxReceiptRows))
                    text.Append('\n').Append(r.Target.Replace("|", "\\|")).Append(" | ").Append(r.Action.Replace("|", "\\|")).Append(" | ").Append(r.Old ?? "-").Append(" | ").Append(r.New ?? "-");
                if (session.Receipts.Count > MaxReceiptRows)
                    text.Append($"\n… {session.Receipts.Count - MaxReceiptRows} more");
            }
            return new QueryResult
            {
                Text = AppendWarnings(text.ToString(), ctx.Warnings),
                Total = items.Count,
                Items = dryRun ? Array.Empty<object?>() : items,
                Warnings = ctx.Warnings,
                Receipts = session.Receipts,
            };
        }

        #endregion

        #region Execution

        internal static ParsedQuery Parse(string query)
        {
            query ??= string.Empty;
            lock (cache)
            {
                if (cache.TryGetValue(query, out var hit))
                {
                    cacheOrder.Remove(query);
                    cacheOrder.AddLast(query);
                    return hit;
                }
            }
            ParsedQuery parsed;
            try
            {
                parsed = QueryParser.Parse(query);
            }
            catch (QueryException e)
            {
                throw new QueryException(e.Describe(query));
            }
            lock (cache)
            {
                cache[query] = parsed;
                cacheOrder.AddLast(query);
                if (cacheOrder.Count > CacheSize)
                {
                    cache.Remove(cacheOrder.First!.Value);
                    cacheOrder.RemoveFirst();
                }
            }
            return parsed;
        }

        static List<object?> Execute(ParsedQuery parsed, QueryContext ctx, string query)
        {
            try
            {
                ctx.AtSource = true;
                ctx.Item = null;
                var source = parsed.Source(ctx);
                ctx.AtSource = false;
                IEnumerable<object?> sequence = source is IEnumerable list && source is not string && source is not Object && source is not Row
                    ? list.Cast<object?>()
                    : new[] { source };
                foreach (var stage in parsed.Stages)
                    sequence = stage.Run(sequence, ctx);
                return sequence.ToList();
            }
            catch (QueryException e)
            {
                throw new QueryException(e.Describe(query));
            }
            finally
            {
                ctx.AtSource = false;
                ctx.Item = null;
            }
        }

        #endregion

        #region Output

        static string Summary(MutationSession session, bool dryRun, string? saved)
        {
            var changes = session.Receipts.Count;
            if (changes == 0)
                return dryRun ? "Dry run: nothing would change." : "Nothing changed.";
            if (dryRun)
                return $"Dry run: nothing was changed. {changes} change(s) would be made:";
            var tail = saved ?? (session.Scenes.Count > 0 || session.Touched.Any(EditorUtility.IsPersistent)
                ? " Not saved yet: pass save=true or save the scene."
                : string.Empty);
            return $"{changes} change(s) made, one undo step ('{UndoName}').{tail}";
        }

        static string Save(MutationSession session, QueryContext ctx)
        {
            var saved = new List<string>();
            foreach (var asset in session.Touched.Where(o => o != null && EditorUtility.IsPersistent(o)).Distinct())
            {
                AssetDatabase.SaveAssetIfDirty(asset);
                saved.Add(AssetDatabase.GetAssetPath(asset));
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            foreach (var scene in session.Scenes)
            {
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                if (stage != null && scene == stage.scene)
                {
                    PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                    saved.Add(stage.assetPath);
                    continue;
                }
                if (Application.isPlaying)
                {
                    ctx.Warn("play mode is on: scenes cannot be saved");
                    continue;
                }
                if (string.IsNullOrEmpty(scene.path))
                {
                    ctx.Warn($"scene '{scene.name}' has never been saved; save it once with scene-save to give it a path");
                    continue;
                }
                if (EditorSceneManager.SaveScene(scene))
                    saved.Add(scene.path);
                else
                    ctx.Warn($"saving '{scene.path}' failed");
            }
            return saved.Count == 0 ? " Nothing needed saving." : $" Saved: {string.Join(", ", saved.Distinct())}.";
        }

        static string AppendWarnings(string text, IReadOnlyList<string> warnings)
            => warnings.Count == 0 ? text : text + "\nwarnings:\n- " + string.Join("\n- ", warnings);

        #endregion
    }
}
