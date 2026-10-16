#nullable enable

using System;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MinMaxCurve = UnityEngine.ParticleSystem.MinMaxCurve;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class VfxStudioTests
    {
        const string Folder = "Assets/_VfxStudioTests";
        const string Fx = Folder + "/Group/Fx.prefab";
        const string Plain = Folder + "/Plain.prefab";
        const string Dup = Folder + "/Dup.prefab";

        const string Spec = @"{ ""emitters"": [
            { ""name"": ""Core"", ""main"": { ""startLifetime"": 0.4, ""startSize"": [1, 2] }, ""emission"": { ""rateOverTime"": 0, ""bursts"": [[0.1, 6]] },
              ""sizeOverLifetime"": { ""size"": ""0:1,1:0.5"" }, ""colorOverLifetime"": { ""color"": ""#FFFFFF@0,#FF0000@1|1@0,0@1"" } },
            { ""name"": ""Debris"", ""emission"": { ""rateOverTime"": 8 } },
            { ""name"": ""Pop"", ""parent"": ""Debris"" } ] }";

        static Param Row(string label, Param[] list) => list.First(p => p.Label == label);

        static ParticleSystem Emitter(VfxEditSession session, string name) => session.Nodes.First(n => n.Transform.name == name).System!;

        [SetUp]
        public void CreateFixtures()
        {
            AssetDatabase.DeleteAsset(Folder);
            VfxService.Apply(Fx, Spec, true, "fixture");
            var go = new GameObject("Plain");
            PrefabUtility.SaveAsPrefabAsset(go, Plain);
            Object.DestroyImmediate(go);
            AssetDatabase.CopyAsset(Fx, Dup);
            var root = PrefabUtility.LoadPrefabContents(Dup);
            try
            {
                root.transform.Find("Debris/Pop").name = "Core";
                PrefabUtility.SaveAsPrefabAsset(root, Dup);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [TearDown]
        public void DeleteFixtures()
        {
            foreach (var path in new[] { Fx, Plain, Dup })
            {
                var history = Path.Combine(VfxHistory.Root, AssetDatabase.AssetPathToGUID(path));
                if (Directory.Exists(history)) Directory.Delete(history, true);
            }
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Library_Scan_ListsParticlePrefabsOnlyAndCountsSubfolders()
        {
            var library = new VfxLibrary();
            library.Scan();

            Assert.AreEqual(3, library.Find(Fx)!.Emitters);
            Assert.IsNull(library.Find(Plain));
            Assert.AreEqual(1, library.CountIn(Folder + "/Group"));
            Assert.AreEqual(2, library.CountIn(Folder));
            Assert.Contains(Folder + "/Group", library.SubFolders(Folder).ToList());
            var group = library.Folders.First(f => f.Path == Folder + "/Group");
            Assert.AreEqual(library.Folders.First(f => f.Path == Folder).Depth + 1, group.Depth);
        }

        [Test]
        public void Library_Scan_ReloadsOnlyThePrefabsWhoseHashChanged()
        {
            new VfxLibrary().Scan();
            var unchanged = new VfxLibrary();
            unchanged.Scan();
            Assert.AreEqual(0, unchanged.Inspected);

            var hashBefore = unchanged.Find(Fx)!.Hash;
            VfxService.Apply(Fx, @"{ ""emitters"": [ { ""name"": ""Debris"", ""emission"": { ""rateOverTime"": 12 } } ] }", false, "change");
            var changed = new VfxLibrary();
            changed.Scan();

            Assert.AreEqual(1, changed.Inspected);
            Assert.AreNotEqual(hashBefore, changed.Find(Fx)!.Hash);
        }

        [Test]
        public void Session_Save_SendsOnlyTheChangedValueAndCreatesAVersion()
        {
            using var session = new VfxEditSession(Fx);
            var before = VfxService.Read(Fx);
            var versionBefore = session.Version;

            Row("Start Lifetime", VfxParams.Emitter).Set(Emitter(session, "Core"), new MinMaxCurve(0.9f));
            session.Touch();
            Assert.AreEqual(1, session.Changes);
            var outcome = session.Save();

            Assert.IsEmpty(outcome.Errors);
            StringAssert.Contains("\"startLifetime\":0.4,", before);
            Assert.AreEqual(before.Replace("\"startLifetime\":0.4,", "\"startLifetime\":0.9,"), VfxService.Read(Fx));
            Assert.AreEqual(0, session.Changes);
            Assert.Greater(session.Version, versionBefore);
            Assert.AreEqual("studio", VfxHistory.List(Fx).Last().Note);
        }

        [Test]
        public void Session_Save_WritesBurstCountAndTimeAsOneBurstList()
        {
            using var session = new VfxEditSession(Fx);
            var core = Emitter(session, "Core");
            var count = Row("Burst Count", VfxParams.Emitter);
            var time = Row("Thời gian phát", VfxParams.Emitter);

            count.Set(core, new MinMaxCurve(9f));
            session.Touch();
            Assert.IsTrue(session.IsChanged(core, Array.IndexOf(VfxParams.Emitter, count)));
            Assert.IsFalse(session.IsChanged(core, Array.IndexOf(VfxParams.Emitter, time)));
            time.Set(core, 0.3f);
            session.Touch();
            session.Save();

            StringAssert.Contains("\"bursts\":[[0.3,9]]", VfxService.Read(Fx));
        }

        [Test]
        public void Session_Save_WritesTheLastKeyOfSizeAndTheLastColorKeyOfColorOverLifetime()
        {
            using var session = new VfxEditSession(Fx);
            var core = Emitter(session, "Core");

            Row("Size cuối đời", VfxParams.Emitter).Set(core, 0.8f);
            Row("Color cuối đời", VfxParams.Emitter).Set(core, new Color(0f, 1f, 0f));
            session.Touch();
            Assert.AreEqual(2, session.Changes);
            session.Save();

            var saved = VfxService.Read(Fx);
            StringAssert.Contains("\"size\":\"0:1,1:0.8\"", saved);
            StringAssert.Contains("\"color\":\"#FFFFFF@0,#00FF00@1|1@0,0@1\"", saved);
        }

        [Test]
        public void Session_Save_WritesOpacityAsTheStartColorAlpha()
        {
            using var session = new VfxEditSession(Fx);

            Row("Opacity", VfxParams.Emitter).Set(Emitter(session, "Core"), 0.5f);
            session.Touch();
            Assert.AreEqual(1, session.Changes);
            session.Save();

            StringAssert.Contains("\"startColor\":\"#FFFFFF80\"", VfxService.Read(Fx));
        }

        [Test]
        public void Params_RowsOfAModuleThatIsOffAreLockedWithTheReason()
        {
            using var session = new VfxEditSession(Fx);
            var debris = Emitter(session, "Debris");

            Assert.AreEqual("tắt", Row("Size cuối đời", VfxParams.Emitter).Locked!(debris));
            Assert.AreEqual("tắt", Row("Color cuối đời", VfxParams.Emitter).Locked!(debris));
            Assert.AreEqual("dùng rate", Row("Burst Count", VfxParams.Emitter).Locked!(debris));
            Assert.IsNull(Row("Burst Count", VfxParams.Emitter).Locked!(Emitter(session, "Core")));
        }

        [Test]
        public void Session_Save_RoundTripsTheRootScaleThroughTheSpec()
        {
            using var session = new VfxEditSession(Fx);

            Row("Scale", VfxParams.Root).Set(session.Root, 2f);
            session.Touch();
            Assert.AreEqual(1, session.Changes);
            session.Save();

            Assert.AreEqual(2f, AssetDatabase.LoadAssetAtPath<GameObject>(Fx).transform.localScale.x);
            StringAssert.Contains("\"root\":{\"scale\":[2,2,2]}", VfxService.Read(Fx));
            VfxService.Apply(Fx, @"{ ""root"": { ""scale"": 1 } }", false, "reset");
            StringAssert.DoesNotContain("\"root\"", VfxService.Read(Fx));
        }

        [Test]
        public void Session_Revert_DropsEditsAndKeepsTheSavedPrefab()
        {
            using var session = new VfxEditSession(Fx);
            var before = VfxService.Read(Fx);

            Row("Start Lifetime", VfxParams.Emitter).Set(Emitter(session, "Core"), new MinMaxCurve(2f));
            session.Touch();
            Assert.AreEqual(1, session.Changes);
            session.Revert();

            Assert.AreEqual(0, session.Changes);
            Assert.AreEqual(0.4f, Emitter(session, "Core").main.startLifetime.constant, 1e-4f);
            Assert.AreEqual(before, VfxService.Read(Fx));
        }

        [Test]
        public void Session_PathOf_JoinsThePrefabPathAndTheTransformPath()
        {
            using var session = new VfxEditSession(Fx);

            Assert.AreEqual(Fx + " > Debris/Pop", session.PathOf(session.Nodes.First(n => n.Path == "Debris/Pop")));
            Assert.AreEqual(Fx + " > Debris", session.PathOf(session.Nodes.First(n => n.Path == "Debris")));
        }

        [Test]
        public void Session_DuplicateEmitterNames_BlockSave()
        {
            using var session = new VfxEditSession(Dup);

            StringAssert.Contains("Core", session.Blocked!);
            Assert.Throws<InvalidOperationException>(() => session.Save());
        }
    }
}
