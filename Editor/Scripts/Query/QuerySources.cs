#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.MCP.Runtime.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    internal static class QuerySources
    {
        public const int MaxAssets = 2000;
        const int MaxLogs = 5000;

        public static readonly string[] Names = { "objects", "roots", "selection", "scenes", "logs", "components", "prefab" };

        public static object? Global(string name, QueryContext ctx, int position)
        {
            switch (name.ToLowerInvariant())
            {
                case "objects": return Objects();
                case "roots": return Roots();
                case "selection": return Selection.objects.Where(o => o != null).Cast<object?>().ToList();
                case "scenes": return OpenScenes().Cast<object?>().ToList();
                case "logs": return Logs();
                case "components": return Components(null);
                case "prefab":
                    var stage = PrefabStageUtility.GetCurrentPrefabStage();
                    if (stage == null)
                        throw new QueryException("No prefab is open in prefab mode", position);
                    return new List<object?> { stage.prefabContentsRoot };
                default:
                    throw new QueryException($"Unknown source '{name}'. Sources: {string.Join(", ", Names)}, or a function like assets(\"t:Material\"), asset(\"Assets/..\"), find(\"Path\")", position);
            }
        }

        static IEnumerable<Scene> OpenScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    yield return scene;
            }
        }

        static IEnumerable<GameObject> RootObjects()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                return new[] { stage.prefabContentsRoot };
            return OpenScenes().SelectMany(s => s.GetRootGameObjects());
        }

        public static List<object?> Roots() => RootObjects().Cast<object?>().ToList();

        public static List<object?> Objects()
        {
            var result = new List<object?>();
            foreach (var root in RootObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    result.Add(t.gameObject);
            return result;
        }

        public static List<object?> Components(Type? type)
        {
            var result = new List<object?>();
            var filter = type ?? typeof(Component);
            if (!typeof(Component).IsAssignableFrom(filter))
                throw new QueryException($"{filter.Name} is not a component type");
            foreach (var root in RootObjects())
                foreach (var c in root.GetComponentsInChildren(filter, true))
                    if (c != null)
                        result.Add(c);
            return result;
        }

        static List<object?> Logs()
        {
            if (!MatrixPluginEditor.HasInstance || MatrixPluginEditor.Instance.LogCollector == null)
                throw new QueryException("The log collector is not running");
            return MatrixPluginEditor.Instance.LogCollector.Query(maxEntries: MaxLogs)
                .Select(e => (object?)new Row(new[] { "time", "type", "message", "count" }, new object?[] { e.Timestamp, e.LogType, e.Message, (long)(e.Count ?? 1) }))
                .ToList();
        }

        public static Object Asset(string path, int position)
            => AssetDatabase.LoadMainAssetAtPath(path) ?? throw new QueryException($"No asset at '{path}'", position);

        public static List<object?> Assets(string filter, string? folder, QueryContext ctx)
        {
            if (folder != null && !AssetDatabase.IsValidFolder(folder))
                throw new QueryException($"'{folder}' is not a folder");
            var guids = folder == null ? AssetDatabase.FindAssets(filter) : AssetDatabase.FindAssets(filter, new[] { folder });
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count > MaxAssets)
            {
                ctx.Warn($"assets(\"{filter}\") matched {paths.Count} assets; only the first {MaxAssets} were loaded. Narrow the filter or pass a folder.");
                paths = paths.Take(MaxAssets).ToList();
            }
            var result = new List<object?>(paths.Count);
            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null)
                    result.Add(asset);
            }
            return result;
        }

        public static GameObject Find(object? key, int position)
        {
            if (key == null)
                throw new QueryException("find() needs a path, a name or an id", position);
            if (QueryOps.IsIntegral(key) || (key is string digits && digits.Length > 0 && digits.All(char.IsDigit)))
            {
                var byId = QueryIds.Resolve(key);
                if (byId is GameObject idObject)
                    return idObject;
                if (byId is Component component)
                    return component.gameObject;
            }
            var text = QueryOps.Text(key).Trim('/');
            var all = Objects().Cast<GameObject>().ToList();
            var byPath = all.Where(go => go.GetPath() == text).ToList();
            if (byPath.Count == 1)
                return byPath[0];
            var byName = byPath.Count > 1 ? byPath : all.Where(go => go.name == text).ToList();
            if (byName.Count == 1)
                return byName[0];
            if (byName.Count > 1)
                throw new QueryException($"find(\"{text}\") is ambiguous: {byName.Count} objects match ({string.Join(", ", byName.Take(5).Select(go => QueryFormat.Cell(go)))}). Use a full path or id.", position);
            throw new QueryException($"find(\"{text}\") found nothing in the open scenes{(PrefabStageUtility.GetCurrentPrefabStage() != null ? " (prefab mode is open: only the prefab is searched)" : string.Empty)}", position);
        }

        public static GameObject New(QueryContext ctx, string name, string? primitive, object? parent, int position)
        {
            if (ctx.Mutation == null)
                throw new QueryException("new() creates objects; use rql-mutate", position);
            GameObject go;
            if (string.IsNullOrEmpty(primitive))
                go = new GameObject(name);
            else
            {
                if (!Enum.TryParse(primitive, true, out PrimitiveType type))
                    throw new QueryException($"Unknown primitive '{primitive}'. Use one of: {string.Join(", ", Enum.GetNames(typeof(PrimitiveType)))}", position);
                go = GameObject.CreatePrimitive(type);
                go.name = name;
            }
            Undo.RegisterCreatedObjectUndo(go, "Matrix query: create");
            var parentTransform = parent switch
            {
                null => PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot.transform,
                GameObject p => p.transform,
                Component c => c.transform,
                string path => Find(path, position).transform,
                _ => throw new QueryException("new() parent must be an object or a path", position),
            };
            if (parentTransform != null)
                Undo.SetTransformParent(go.transform, parentTransform, "Matrix query: create");
            ctx.Mutation.NoteScene(go);
            ctx.Mutation.Record(go, "create", null, go.GetPath());
            return go;
        }
    }
}
