#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Feeder.MCP.Editor.Tests.Tools
{
    // one regression test per bug found by the tool test run
    public sealed class ReportFixTests
    {
        const string Folder = "Assets/_MxTestReport";
        const string TypeName = "Feeder.MCP.Editor.Tests.Tools.MatrixTestData";

        [SetUp]
        public void SetUp() => Mx.EnsureFolder(Folder);

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            if (Directory.Exists("MxTraversal"))
                Directory.Delete("MxTraversal", true);
        }

        static void NoFile(string path) => Assert.IsFalse(File.Exists(path), path + " must not be created");

        [Test]
        public void Bug1_SerializedSet_DotDotCannotLeaveAssets()
        {
            Assert.Throws<ToolCallerException>(() => new Tool_Serialized().Set("Assets/../MxTraversal/T.asset", null, TypeName));
            Assert.IsFalse(Directory.Exists("MxTraversal"), "no folder is created outside Assets");
        }

        [Test]
        public void Bug2_ScreenshotSavePathDot_IsRefusedBeforeAnyWrite()
        {
            var outside = Path.GetFullPath(MatrixPluginEditor.ProjectRootPath).TrimEnd('/', '\\') + ".png";
            var before = File.Exists(outside) ? File.GetLastWriteTimeUtc(outside) : (DateTime?)null;

            Assert.Throws<ToolCallerException>(() => new Tool_Screenshot().ScreenshotGameView(savePath: "."));

            Assert.AreEqual(before, File.Exists(outside) ? File.GetLastWriteTimeUtc(outside) : (DateTime?)null);
        }

        [Test]
        public void Bug3_SerializedGet_PathToAStringReturnsIt()
        {
            var result = new Tool_Serialized().Get("player", "productName");
            Assert.AreEqual(1, result.Properties.Count);
            StringAssert.Contains(PlayerSettings.productName, result.Properties[0].Value);
        }

        [Test]
        public void Bug4_UndoAfterCreateType_LeavesALoadableAsset()
        {
            var path = Folder + "/Undo.asset";
            Undo.IncrementCurrentGroup();
            new Tool_Serialized().Set(path, new[] { "hidden=3" }, TypeName);

            Undo.PerformUndo();
            Undo.PerformUndo();

            Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(path), "the created file stays a valid asset");
        }

        static CurveKey[] Keys => new[] { new CurveKey { Time = 0, Value = 0 }, new CurveKey { Time = 1, Value = 1 } };

        [Test]
        public void Bug5_BadCurveInput_CreatesNoClip()
        {
            Assert.Throws<ArgumentException>(() => Tool_Animation.ClipSetCurve(Folder + "/K.anim", "", "Transform", "m_LocalPosition.x", Array.Empty<CurveKey>()));
            NoFile(Folder + "/K.anim");
            Assert.Throws<ArgumentException>(() => Tool_Animation.ClipSetCurve(Folder + "/T.anim", "", "NoSuchComponentType", "m_X", Keys));
            NoFile(Folder + "/T.anim");
            Assert.Throws<ArgumentException>(() => Tool_Animation.ClipSetCurve(Folder + "/P.anim", "", "Transform", "", Keys));
            NoFile(Folder + "/P.anim");
            Assert.Throws<ArgumentException>(() => Tool_Animation.ClipSetCurve(Folder + "/I.anim", "", "Transform", "m_LocalPosition.x", Keys, "bogus"));
            NoFile(Folder + "/I.anim");
            Assert.Throws<ToolCallerException>(() => Tool_Animation.ClipSetCurve(Folder + "/R.anim", "", "Transform", "m_LocalPosition.x", remove: true));
            NoFile(Folder + "/R.anim");
        }

        [Test]
        public void Bug5_BadControllerOps_CreateNoController()
        {
            Assert.Throws<ArgumentException>(() => Tool_Animation.ControllerEdit(Folder + "/C.controller", new[] { new ControllerOp { Op = "bogus-op" } }));
            NoFile(Folder + "/C.controller");
            Assert.Throws<ArgumentException>(() => Tool_Animation.ControllerEdit(Folder + "/D.controller", new[] { new ControllerOp { Op = "add-state" } }));
            NoFile(Folder + "/D.controller");
        }

        [Test]
        public void Bug6_AFileThatIsNotAClip_IsNeverOverwritten()
        {
            var path = Folder + "/Bad.anim";
            File.WriteAllText(path, "not a clip");

            Assert.Throws<ToolCallerException>(() => Tool_Animation.ClipSetCurve(path, "", "Transform", "m_LocalPosition.x", Keys));

            Assert.AreEqual("not a clip", File.ReadAllText(path));
        }

        [Test]
        public void Bug7_RefreshInPlayMode_AnswersAtOnce()
        {
            var original = MxGuard.IsPlaying;
            try
            {
                MxGuard.IsPlaying = () => true;
                var call = new Tool_Assets().Refresh(ImportAssetOptions.Default, "mx-test");

                // never block on it here: a refresh that waits for a compile would deadlock the main thread
                Assert.IsTrue(call.IsCompleted, "in play mode the refresh must answer at once, not wait for a compile");
                var response = call.Result;
                Assert.AreEqual(ResponseStatus.Success, response.Status);
                StringAssert.Contains("held until play mode ends", response.GetMessage());
            }
            finally
            {
                MxGuard.IsPlaying = original;
            }
        }

        // the description says "after [Success] skip console-get-logs", so a refresh that compiled nothing must carry it too
        [UnityTest]
        public IEnumerator Refresh_WithNothingToCompile_AnswersWithTheSuccessMarker()
        {
            var call = new Tool_Assets().Refresh(ImportAssetOptions.Default, "mx-test-idle");
            var until = EditorApplication.timeSinceStartup + 5;
            while (!call.IsCompleted && EditorApplication.timeSinceStartup < until)
                yield return null;

            Assert.IsTrue(call.IsCompleted, "the refresh did not answer within 5 s");
            var response = call.Result;
            if (response.Status == ResponseStatus.Processing || EditorUtility.scriptCompilationFailed)
                Assert.Ignore("A compile started or failed, so this refresh had something to compile.");
            Assert.AreEqual(Tool_Assets.NothingToCompileAnswer, response.GetMessage());
            StringAssert.StartsWith("[Success]", response.GetMessage());
            StringAssert.Contains("after [Success]", typeof(Tool_Assets).GetMethod(nameof(Tool_Assets.Refresh))!
                .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
                .Cast<System.ComponentModel.DescriptionAttribute>().Single().Description);
        }

        [Test]
        public void Bug8_Undo_ReportsTheNextRedoGroup()
        {
            var target = ScriptableObject.CreateInstance<MatrixTestData>();
            try
            {
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(target, "MxRedo A");
                target.name = "a";
                Undo.SetCurrentGroupName("MxRedo A");
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(target, "MxRedo B");
                target.name = "b";
                Undo.SetCurrentGroupName("MxRedo B");
                Undo.IncrementCurrentGroup();

                var result = new Tool_Editor().UndoRedo("undo", 2);

                Assert.AreEqual(2, result.Performed);
                Assert.AreEqual("MxRedo A", result.NextRedo);
            }
            finally
            {
                Undo.ClearUndo(target);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
