#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Feeder.MCP.Editor.API;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor
{
    // the jobs agents script most often, each with its guards and Undo built in; tools call these too
    public static class Mx
    {
        #region Scenes

        public static Scene OpenScene(string path, bool additive = false, string ifDirty = MxGuard.Refuse)
            => OpenScene(path, additive ? OpenSceneMode.Additive : OpenSceneMode.Single, ifDirty);

        internal static Scene OpenScene(string path, OpenSceneMode mode, string ifDirty)
        {
            path = MxGuard.ReadableAssetPath(path, ".unity");
            MxGuard.IfDirty(ifDirty);
            return MainThread.Instance.Run(() =>
            {
                MxGuard.NotPlaying("open a scene");
                if (!File.Exists(path) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                    throw MxGuard.Fail($"No scene asset at '{path}'. Check the path with rql-query: assets(\"t:Scene\").");

                var open = SceneManager.GetSceneByPath(path);
                if (open.IsValid() && open.isLoaded)
                {
                    EditorSceneManager.SetActiveScene(open);
                    return open;
                }
                if (mode == OpenSceneMode.Single)
                    MxGuard.ScenesSaved(ifDirty);
                var scene = EditorSceneManager.OpenScene(path, mode);
                if (!scene.IsValid())
                    throw new InvalidOperationException($"Unity failed to open '{path}'.");
                return scene;
            });
        }

        public static string SaveScene(Scene scene)
        {
            return MainThread.Instance.Run(() =>
            {
                MxGuard.NotPlaying("save a scene");
                if (!scene.IsValid())
                    throw MxGuard.Fail("The scene is not valid. Pass an open scene, e.g. SceneManager.GetSceneByPath(path).");
                if (string.IsNullOrEmpty(scene.path))
                    throw MxGuard.Fail($"Scene '{scene.name}' is untitled. Save it with scene-save and a path, or create it with Mx.NewScene(path).");
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException($"Unity failed to save '{scene.path}'.");
                return scene.path;
            });
        }

        public static Scene NewScene(string path, string ifDirty = MxGuard.Refuse, bool additive = false, bool empty = false)
        {
            path = MxGuard.AssetPath(path, ".unity");
            MxGuard.IfDirty(ifDirty);
            return MainThread.Instance.Run(() =>
            {
                MxGuard.NotPlaying("create a scene");
                MxGuard.NotExisting(path);
                if (additive && Enumerable.Range(0, SceneManager.sceneCount).Any(i => string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)))
                    throw MxGuard.Fail("An untitled scene is open and Unity refuses an additive scene next to it. Save that scene first, or create this one with additive=false.");
                if (!additive)
                    MxGuard.ScenesSaved(ifDirty);
                EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));

                var scene = EditorSceneManager.NewScene(empty ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects,
                    additive ? NewSceneMode.Additive : NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new InvalidOperationException($"Unity failed to save the new scene to '{path}'.");
                return scene;
            });
        }

        #endregion

        #region Assets

        // asset files are not undoable: Undo would leave a broken file on disk
        public static string CreateAsset(UnityEngine.Object asset, string path)
        {
            if (asset == null)
                throw MxGuard.Fail("asset is null. Pass the object to save, e.g. new Material(shader).");
            path = MxGuard.AssetPath(path);
            MxGuard.NotExisting(path);
            return MainThread.Instance.Run(() =>
            {
                EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
                AssetDatabase.CreateAsset(asset, path);
                return path;
            });
        }

        public static string EnsureFolder(string path)
        {
            // the root itself always exists; an asset directly under Assets/ has it as its folder
            if ((path ?? string.Empty).Replace('\\', '/').TrimEnd('/') == "Assets")
                return "Assets";
            path = MxGuard.AssetPath(path);
            return MainThread.Instance.Run(() =>
            {
                var current = "Assets";
                foreach (var part in path.Split('/').Skip(1))
                {
                    var next = current + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        if (File.Exists(next))
                            throw MxGuard.Fail($"'{next}' is a file, not a folder. Pass another path.");
                        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, part)))
                            throw new InvalidOperationException($"Unity failed to create folder '{next}'.");
                    }
                    current = next;
                }
                return path;
            });
        }

        // moves to the OS trash, so the user can still get it back
        public static void TrashAsset(string path)
        {
            path = MxGuard.AssetPath(path);
            MainThread.Instance.Run(() =>
            {
                if (!File.Exists(path) && !AssetDatabase.IsValidFolder(path))
                    throw MxGuard.Fail($"Nothing at '{path}'. Check the path with rql-query: asset(\"{path}\").");
                if (!AssetDatabase.MoveAssetToTrash(path))
                    throw new InvalidOperationException($"Unity failed to move '{path}' to the trash.");
            });
        }

        #endregion

        #region Prefabs

        // keeps the prefab link; one Undo step
        public static GameObject Instantiate(string prefabPath, Transform? parent = null)
        {
            prefabPath = MxGuard.ReadableAssetPath(prefabPath, ".prefab");
            return MainThread.Instance.Run(() =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    throw MxGuard.Fail($"No prefab at '{prefabPath}'. Check the path with rql-query: assets(\"t:Prefab\").");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {prefab.name}");
                return instance;
            });
        }

        // edits the prefab asset in isolation and saves it; nothing is left loaded when edit throws
        public static void EditPrefab(string prefabPath, Action<GameObject> edit)
        {
            prefabPath = MxGuard.AssetPath(prefabPath, ".prefab");
            if (edit == null)
                throw MxGuard.Fail("edit is null. Pass the change, e.g. root => root.name = \"Hero\".");
            MainThread.Instance.Run(() =>
            {
                if (!File.Exists(prefabPath))
                    throw MxGuard.Fail($"No prefab at '{prefabPath}'.");
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    edit(root);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var saved);
                    if (!saved)
                        throw new InvalidOperationException($"Unity failed to save '{prefabPath}'.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            });
        }

        #endregion

        #region Calls and captures

        // calls a method by name, private or static too; target is an instance or a Type
        public static object? Call(object target, string method, params object?[] args)
        {
            if (target == null)
                throw MxGuard.Fail("target is null. Pass an instance, e.g. Object.FindFirstObjectByType<Boot>(), or a Type, e.g. typeof(Boot).");
            MxGuard.NotEmpty(method, "method", "ResetProgress");
            args ??= new object?[] { null };
            var type = target as Type ?? target.GetType();
            var instance = target is Type ? null : target;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | (instance != null ? BindingFlags.Instance : 0);

            var candidates = Enumerable.Empty<MethodInfo>();
            for (var t = type; t != null && !candidates.Any(); t = t.BaseType)
                candidates = t.GetMethods(flags | BindingFlags.DeclaredOnly).Where(m => m.Name == method).ToArray();
            var all = candidates.ToArray();
            if (all.Length == 0)
                throw MxGuard.Fail($"{type.FullName} has no method '{method}'" + (instance == null ? " that is static. Pass an instance for instance methods." : "."));
            var fits = all.Where(m => Fits(m.GetParameters(), args)).ToArray();
            if (fits.Length != 1)
                throw MxGuard.Fail($"{(fits.Length == 0 ? "No" : "More than one")} overload of {type.Name}.{method} takes these {args.Length} argument(s). Overloads: " +
                    string.Join("; ", all.Select(m => $"({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})")));

            var chosen = fits[0];
            var parameters = chosen.GetParameters();
            var full = parameters.Select((p, i) => i < args.Length ? args[i] : p.DefaultValue).ToArray();
            return MainThread.Instance.Run(() =>
            {
                try { return chosen.Invoke(instance, full); }
                catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
            });
        }

        static bool Fits(ParameterInfo[] parameters, object?[] args)
        {
            if (args.Length > parameters.Length || parameters.Skip(args.Length).Any(p => !p.IsOptional))
                return false;
            for (var i = 0; i < args.Length; i++)
            {
                var type = parameters[i].ParameterType;
                if (args[i] == null ? type.IsValueType && Nullable.GetUnderlyingType(type) == null : !type.IsInstanceOfType(args[i]))
                    return false;
            }
            return true;
        }

        // saves a PNG inside the project: the Game View, or what the camera renders; returns the full path
        public static string Shot(string path, Camera? camera = null)
        {
            var full = MxGuard.PngFile(path);
            return MainThread.Instance.Run(() =>
            {
                string error;
                var png = camera == null
                    ? Tool_Screenshot.CaptureGameView(0, out _, out _, out error)
                    : Tool_Screenshot.CaptureCamera(camera, 0, out _, out _, out error);
                if (png == null)
                    throw MxGuard.Fail(error + " Pass a camera to render it instead.");
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, png);
                return full;
            });
        }

        #endregion
    }
}
