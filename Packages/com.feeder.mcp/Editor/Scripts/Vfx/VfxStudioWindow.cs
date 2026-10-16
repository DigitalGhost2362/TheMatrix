#nullable enable
using System;
using System.Collections.Generic;
using Feeder.Json.Nodes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Vfx
{
    // the human side of the vfx tools: library, live-preview editor that saves through VfxService, and the texture generator
    public sealed class VfxStudioWindow : EditorWindow
    {
        const float TabHeight = 20f;
        const float FrameSeconds = 1f / 60f;
        const double ToastSeconds = 2.2;

        static readonly string[] Tabs = { "Effect", "Textures" };

        static readonly Dictionary<string, string> RecipeTemplates = new Dictionary<string, string>
        {
            ["glow"] = "{\"shape\":\"glow\",\"falloff\":2}",
            ["circle"] = "{\"shape\":\"circle\",\"soft\":0.3,\"hollow\":0}",
            ["ring"] = "{\"shape\":\"ring\",\"radius\":0.7,\"width\":0.15,\"soft\":0.6}",
            ["spark"] = "{\"shape\":\"spark\",\"thin\":0.12,\"falloff\":1.5}",
            ["star"] = "{\"shape\":\"star\",\"points\":4,\"inner\":0.3,\"sharp\":6,\"glow\":0.4}",
            ["flare"] = "{\"shape\":\"flare\",\"rays\":2,\"thin\":0.05,\"glow\":0.3}",
            ["smoke"] = "{\"shape\":\"smoke\",\"size\":512,\"frames\":[4,4],\"scale\":4,\"seed\":1}",
            ["noise"] = "{\"shape\":\"noise\",\"size\":256,\"scale\":4,\"contrast\":1.5}",
            ["trail"] = "{\"shape\":\"trail\",\"soft\":0.6}",
            ["shockwave"] = "{\"shape\":\"shockwave\",\"radius\":0.75,\"width\":0.12}",
            ["polygon"] = "{\"shape\":\"polygon\",\"sides\":3,\"soft\":0.1}",
            ["lightning"] = "{\"shape\":\"lightning\",\"size\":256,\"frames\":[2,2],\"thin\":0.08}",
        };

        [SerializeField] VfxLibraryView libraryView = new VfxLibraryView();
        [SerializeField] string openPath = "";
        readonly VfxLibrary library = new VfxLibrary();
        readonly VfxEditView editView = new VfxEditView();
        VfxEditSession? session;
        bool ticking;
        double lastTick;

        int tab;
        Vector2 scroll;
        string texName = "my_texture";
        int shapeIndex;
        string recipeText = RecipeTemplates["glow"];
        Texture2D? texPreview;
        string message = "";

        public static void ShowWindow() => GetWindow<VfxStudioWindow>("VFX Studio");

        void OnEnable()
        {
            minSize = new Vector2(780f, 460f);
            wantsMouseMove = true;
            saveChangesMessage = "VFX Studio có thay đổi chưa lưu. Lưu vào prefab?";
            libraryView.OnRefresh = Refresh;
            editView.OnBack = () => Defer(Back);
            editView.OnSave = () => Defer(() => Save());
            editView.OnRevert = () => Defer(() => session?.Revert());
            editView.OnPlay = EnsureTick;
            editView.OnToast = Toast;
            Defer(Startup);
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            ticking = false;
            session?.Dispose();
            session = null;
            libraryView.Dispose();
            DestroyTex(ref texPreview);
        }

        public override void SaveChanges()
        {
            Save();
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            session?.Revert();
            base.DiscardChanges();
        }

        void OnGUI()
        {
            tab = GUI.Toolbar(new Rect(0f, 0f, position.width, TabHeight), tab, Tabs);
            GUILayout.Space(TabHeight);
            if (tab == 0)
            {
                DrawEffect(new Rect(0f, TabHeight, position.width, position.height - TabHeight));
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawTextures();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        #region Effect

        void DrawEffect(Rect area)
        {
            if (Event.current.type == EventType.MouseMove) Repaint();
            if (session == null)
            {
                var chosen = libraryView.Draw(area, library);
                if (libraryView.HasPending) EnsureTick();
                if (chosen != null)
                {
                    var path = chosen.Path;
                    Defer(() => Open(path));
                }
                return;
            }
            editView.Draw(area, session);
            var dirty = session.Changes > 0;
            if (hasUnsavedChanges != dirty) hasUnsavedChanges = dirty;
        }

        void Startup()
        {
            library.Scan();
            libraryView.Prune(library);
            if (openPath.Length > 0 && session == null) Open(openPath);
        }

        void Refresh() => Defer(() =>
        {
            library.Scan();
            libraryView.Prune(library);
        });

        void Open(string path)
        {
            try
            {
                var next = new VfxEditSession(path);
                session?.Dispose();
                session = next;
                openPath = path;
                EnsureTick();
            }
            catch (Exception ex)
            {
                openPath = "";
                Debug.LogWarning($"[Matrix VFX] cannot open {path}: {ex.Message}");
                Toast("Không mở được prefab, xem Console");
            }
        }

        void Back()
        {
            if (session == null) return;
            if (session.Changes > 0)
            {
                var choice = EditorUtility.DisplayDialogComplex("VFX Studio", $"{session.Changes} thay đổi chưa lưu trong {session.Name}.", "Lưu", "Hủy", "Bỏ");
                if (choice == 1 || (choice == 0 && !Save())) return;
            }
            session.Dispose();
            session = null;
            openPath = "";
            hasUnsavedChanges = false;
        }

        bool Save()
        {
            if (session == null) return false;
            try
            {
                var outcome = session.Save();
                library.Refresh(session.PrefabPath);
                var item = library.Find(session.PrefabPath);
                if (item != null) libraryView.Forget(item);
                if (outcome.Errors.Count > 0) Debug.LogWarning("[Matrix VFX] " + outcome.Summary(session.PrefabPath));
                Toast(outcome.Errors.Count == 0 ? $"v{outcome.Version} đã lưu" : $"v{outcome.Version} đã lưu, {outcome.Errors.Count} lỗi (xem Console)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Matrix VFX] " + ex.Message);
                Toast("Lưu thất bại: " + ex.Message);
                return false;
            }
        }

        void Toast(string text) => ShowNotification(new GUIContent(text), ToastSeconds);

        // delayCall stalls while the editor has no focus, update does not
        void Defer(Action action)
        {
            void Run()
            {
                EditorApplication.update -= Run;
                if (this == null) return;
                action();
                Repaint();
            }
            EditorApplication.update += Run;
        }

        void EnsureTick()
        {
            if (ticking) return;
            ticking = true;
            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            var delta = (float)(now - lastTick);
            if (delta < FrameSeconds) return;
            lastTick = now;
            bool keep;
            if (session != null)
            {
                keep = session.Preview.Animating;
                if (keep) Repaint();
            }
            else
            {
                if (libraryView.Pump()) Repaint();
                keep = libraryView.HasPending;
            }
            if (keep) return;
            EditorApplication.update -= Tick;
            ticking = false;
        }

        #endregion

        #region Textures

        void DrawTextures()
        {
            EditorGUILayout.LabelField("Procedural texture", EditorStyles.boldLabel);
            texName = EditorGUILayout.TextField("Name", texName);
            EditorGUI.BeginChangeCheck();
            shapeIndex = EditorGUILayout.Popup("Shape", shapeIndex, VfxTextures.Shapes);
            if (EditorGUI.EndChangeCheck()) recipeText = RecipeTemplates[VfxTextures.Shapes[shapeIndex]];
            EditorGUILayout.LabelField("Recipe (JSON)");
            recipeText = EditorGUILayout.TextArea(recipeText, GUILayout.MinHeight(40));
            if (GUILayout.Button("Generate"))
                Guard(() =>
                {
                    var path = VfxTextures.Create(texName, VfxService.ParseObject(recipeText, "recipe"));
                    DestroyTex(ref texPreview);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    message = $"{path} - use it as \"material\":\"{texName}/add\"";
                    EditorGUIUtility.PingObject(tex);
                    Load(ref texPreview, System.IO.File.ReadAllBytes(path));
                });
            if (texPreview != null) DrawImage(texPreview, 256);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Library (" + VfxTextures.Folder + ")", EditorStyles.boldLabel);
            if (!AssetDatabase.IsValidFolder(VfxTextures.Folder)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { VfxTextures.Folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40));
                    EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));
                    if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(tex != null ? tex.name : path, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(VfxTextures.RecipeOf(tex) ?? "", EditorStyles.wordWrappedMiniLabel);
                    }
                    if (GUILayout.Button("Edit", GUILayout.Width(40)))
                    {
                        texName = tex != null ? tex.name : texName;
                        recipeText = VfxTextures.RecipeOf(tex) ?? recipeText;
                        var shape = (string?)(JsonNode.Parse(recipeText) as JsonObject)?["shape"];
                        shapeIndex = Math.Max(0, Array.IndexOf(VfxTextures.Shapes, shape));
                    }
                }
            }
        }

        #endregion

        #region Helpers

        void Guard(Action action)
        {
            try
            {
                message = "";
                action();
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Debug.LogWarning("[Matrix VFX] " + ex.Message);
            }
        }

        void DrawImage(Texture2D tex, float maxWidth = float.MaxValue)
        {
            var width = Mathf.Min(position.width - 20f, maxWidth, tex.width);
            var rect = GUILayoutUtility.GetRect(width, width * tex.height / tex.width, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
        }

        static void Load(ref Texture2D? target, byte[]? png)
        {
            DestroyTex(ref target);
            if (png == null) return;
            target = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            target.LoadImage(png);
        }

        static void DestroyTex(ref Texture2D? tex)
        {
            if (tex != null) Object.DestroyImmediate(tex);
            tex = null;
        }

        #endregion
    }
}
