#nullable enable

using System;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost.Common.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class UiToolkitTests
    {
        const string Folder = "Assets/_MxTest3D8";
        readonly Tool_UiToolkit tool = new Tool_UiToolkit();
        Scene previousActive;
        Scene temp;

        [SetUp]
        public void SetUp()
        {
            previousActive = SceneManager.GetActiveScene();
            temp = default;
            if (!string.IsNullOrEmpty(previousActive.path))
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temp);
            }
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "_MxTest3D8");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name.StartsWith("MxTest"))
                    UnityEngine.Object.DestroyImmediate(root);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (temp.IsValid())
                EditorSceneManager.CloseScene(temp, true);
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Validate_Uxml_RejectsBrokenXml_AndWrongRoot()
        {
            Assert.IsNull(Tool_UiToolkit.Validate(Tool_UiToolkit.UxmlTemplate, true));
            StringAssert.Contains("not well-formed", Tool_UiToolkit.Validate("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label></ui:UXML>", true));
            StringAssert.Contains("root element", Tool_UiToolkit.Validate("<html/>", true));
        }

        [Test]
        public void Validate_Uss_ChecksBraces_IgnoringComments()
        {
            Assert.IsNull(Tool_UiToolkit.Validate(".a { color: red; } /* } */", false));
            Assert.IsNotNull(Tool_UiToolkit.Validate(".a { color: red;", false));
            Assert.IsNotNull(Tool_UiToolkit.Validate("} .a { }", false));
        }

        [Test]
        public void Replace_NeedsExactlyOneMatch()
        {
            Assert.AreEqual("a-X-c", Tool_UiToolkit.Replace("a-b-c", "b", "X"));
            Assert.Throws<ArgumentException>(() => Tool_UiToolkit.Replace("a-b-b", "b", "X"));
            Assert.Throws<ArgumentException>(() => Tool_UiToolkit.Replace("abc", "z", "X"));
        }

        [Test]
        public void RequireAssetPath_RejectsOutsideAssets_AndWrongExtension()
        {
            Assert.Throws<ArgumentException>(() => Tool_UiToolkit.RequireAssetPath("C:/x/a.uxml", ".uxml"));
            Assert.Throws<ArgumentException>(() => Tool_UiToolkit.RequireAssetPath("Assets/../a.uxml", ".uxml"));
            Assert.Throws<ArgumentException>(() => Tool_UiToolkit.RequireAssetPath("Assets/a.txt", ".uxml", ".uss"));
        }

        [Test]
        public void Write_CreateThenEdit_ImportsAndModifies()
        {
            var uxml = Folder + "/Hud.uxml";
            StringAssert.Contains("created", tool.UiToolkitWrite(uxml));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml));
            Assert.Throws<InvalidOperationException>(() => tool.UiToolkitWrite(uxml));

            tool.UiToolkitWrite(uxml, "<ui:Label name=\"title\" text=\"Hi\" />", "replace", "<ui:VisualElement name=\"root\" />");
            StringAssert.Contains("name=\"title\"", File.ReadAllText(uxml));

            var uss = Folder + "/Hud.uss";
            tool.UiToolkitWrite(uss, ".title { color: red; }");
            tool.UiToolkitWrite(uss, ".other { color: blue; }", "append");
            var text = File.ReadAllText(uss);
            StringAssert.Contains(".title", text);
            StringAssert.Contains(".other", text);
        }

        [Test]
        public void Write_InvalidContent_WritesNothing()
        {
            var uxml = Folder + "/Bad.uxml";
            Assert.Throws<ArgumentException>(() => tool.UiToolkitWrite(uxml, "<ui:UXML><oops></ui:UXML>"));
            Assert.IsFalse(File.Exists(uxml));
        }

        [Test]
        public void AttachDocument_CreatesObjectDocumentAndPanelSettings_AsOneUndoStep()
        {
            var uxml = Folder + "/Hud.uxml";
            tool.UiToolkitWrite(uxml);
            var panel = Folder + "/Panel.asset";
            Undo.IncrementCurrentGroup();

            var result = tool.AttachDocument("MxTestUi", uxml, panel, 3f);

            StringAssert.Contains("created MxTestUi", result);
            var go = GameObject.Find("MxTestUi");
            var doc = go.GetComponent<UIDocument>();
            Assert.AreEqual(uxml, AssetDatabase.GetAssetPath(doc.visualTreeAsset));
            Assert.AreEqual(panel, AssetDatabase.GetAssetPath(doc.panelSettings));
            Assert.AreEqual(3f, doc.sortingOrder);

            StringAssert.Contains("updated MxTestUi", tool.AttachDocument("MxTestUi", uxml, panel));
            Undo.PerformUndo();
            Undo.PerformUndo();
            Assert.IsNull(GameObject.Find("MxTestUi"));
        }

        [Test]
        public void AttachDocument_MissingUxml_ChangesNothing()
        {
            Assert.Throws<ArgumentException>(() => tool.AttachDocument("MxTestUi", Folder + "/none.uxml"));
            Assert.IsNull(GameObject.Find("MxTestUi"));
        }

        static void IgnoreIfUnsupported(ResponseCallTool response)
        {
            if (response.Status != ResponseStatus.Success && (response.GetMessage() ?? string.Empty).Contains("[unsupported]"))
                Assert.Ignore(response.GetMessage());
        }

        [Test]
        public void Render_Uxml_ReturnsImage_AndLeavesNoTemporaryDocuments()
        {
            var uxml = Folder + "/Hud.uxml";
            tool.UiToolkitWrite(uxml, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"Matrix\" style=\"font-size: 40px; color: white;\" /></ui:UXML>");
            var before = Resources.FindObjectsOfTypeAll<UIDocument>().Length;
            var panelsBefore = Resources.FindObjectsOfTypeAll<PanelSettings>().Length;

            var response = tool.Render(uxml, 320, 200, maxResolution: 0);

            IgnoreIfUnsupported(response);
            Assert.AreEqual(ResponseStatus.Success, response.Status);
            Assert.IsTrue(response.Content.Any(c => c.Type == "image"));
            Assert.AreEqual(before, Resources.FindObjectsOfTypeAll<UIDocument>().Length);
            Assert.AreEqual(panelsBefore, Resources.FindObjectsOfTypeAll<PanelSettings>().Length);
        }

        [Test]
        public void Render_ColoredElement_IsNotAFlatColor()
        {
            var uxml = Folder + "/Color.uxml";
            tool.UiToolkitWrite(uxml, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement style=\"position: absolute; left: 0; top: 0; width: 50%; height: 50%; background-color: rgb(255, 0, 0);\" /></ui:UXML>");

            var response = tool.Render(uxml, 128, 128, maxResolution: 0);

            IgnoreIfUnsupported(response);
            Assert.AreEqual(ResponseStatus.Success, response.Status, response.GetMessage());
            var image = response.Content.First(c => c.Type == "image");
            var tex = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(tex.LoadImage(Convert.FromBase64String(image.Data!)));
                var pixels = tex.GetPixels32();
                Assert.IsTrue(pixels.Any(p => p.r != pixels[0].r || p.g != pixels[0].g || p.b != pixels[0].b), "the rendered image is a single flat color");
                // Texture2D rows run bottom to top, so an upright image has the top-left element in the upper rows
                var top = tex.GetPixel(tex.width / 8, tex.height * 7 / 8);
                var bottom = tex.GetPixel(tex.width / 8, tex.height / 8);
                Assert.Greater(top.r, 0.8f, "the red element is not at the top-left (image upside down?)");
                Assert.Less(top.g, 0.2f);
                Assert.Less(bottom.r, 0.6f, "the bottom-left corner is red (image upside down?)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void Render_SceneDocument_RestoresItsPanelSettings()
        {
            var uxml = Folder + "/Hud.uxml";
            tool.UiToolkitWrite(uxml);
            var panel = Folder + "/Panel.asset";
            tool.AttachDocument("MxTestUi", uxml, panel);
            var doc = GameObject.Find("MxTestUi").GetComponent<UIDocument>();
            var settings = doc.panelSettings;

            var response = tool.Render("MxTestUi", 128, 128, maxResolution: 0);

            IgnoreIfUnsupported(response);
            Assert.AreEqual(ResponseStatus.Success, response.Status);
            Assert.AreSame(settings, doc.panelSettings);
            Assert.IsNull(settings.targetTexture);
        }

        [Test]
        public void Render_BadInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => tool.Render("NoSuchObject"));
            Assert.Throws<ArgumentException>(() => tool.Render("Assets/none.uxml"));
            Assert.Throws<ArgumentException>(() => tool.Render("Assets/x.uxml", 4, 4));
        }
    }
}
