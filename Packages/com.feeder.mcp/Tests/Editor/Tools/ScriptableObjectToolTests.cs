#nullable enable

using System;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ScriptableObjectToolTests
    {
        const string Root = "Assets/_MxTestG2";
        const string Asset = Root + "/Sub/Data.asset";

        readonly Tool_ScriptableObject tool = new Tool_ScriptableObject();
        const string TypeName = "Feeder.MCP.Editor.Tests.Tools.MatrixTestData";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
            if (File.Exists(Root + ".meta"))
                File.Delete(Root + ".meta");
            AssetDatabase.Refresh();
        }

        static MatrixTestData Load() => AssetDatabase.LoadAssetAtPath<MatrixTestData>(Asset);

        [Test]
        public void Create_MakesTheAssetWithFolders_AndFillsPrivateFields()
        {
            var result = tool.Create("MatrixTestData", Asset, new[] { "hidden=7", "kind=Beta", "offset=1,2,3", "tint=#FF0000" });

            Assert.AreEqual(0, result.Failed);
            Assert.AreEqual(TypeName, result.Type);
            var data = Load();
            Assert.IsNotNull(data);
            Assert.AreEqual(7, data.Hidden);
            Assert.AreEqual(MatrixTestKind.Beta, data.Kind);
            Assert.AreEqual(new Vector3(1, 2, 3), data.Offset);
            Assert.AreEqual(Color.red, data.Tint);
        }

        [Test]
        public void Create_NeverOverwritesAndChecksTheType()
        {
            tool.Create(TypeName, Asset);

            Assert.Throws<ArgumentException>(() => tool.Create(TypeName, Asset));
            Assert.Throws<ArgumentException>(() => tool.Create("NoSuchScriptableObjectType", Root + "/Other.asset"));
            Assert.Throws<ArgumentException>(() => tool.Create(TypeName, "Packages/x/Other.asset"));
            Assert.Throws<ArgumentException>(() => tool.Create(TypeName, Root + "/Other.txt"));
        }

        [Test]
        public void Set_ResizesArraysAndFillsElements()
        {
            tool.Create(TypeName, Asset);

            var result = tool.Set(Asset, new[]
            {
                "names.Array.size=3",
                "names.Array.data[1]=middle",
                "entries.Array.size=2",
                "entries.Array.data[0].label=Sword",
                "entries.Array.data[0].amount=5",
            });

            Assert.AreEqual(0, result.Failed, string.Join(" | ", result.Lines));
            var data = Load();
            Assert.AreEqual(3, data.Names.Length);
            Assert.AreEqual("middle", data.Names[1]);
            Assert.AreEqual(2, data.Entries.Count);
            Assert.AreEqual("Sword", data.Entries[0].label);
            Assert.AreEqual(5, data.Entries[0].amount);
            Assert.AreEqual("Matrix scriptableobject set", Undo.GetCurrentGroupName());
        }

        [Test]
        public void Set_ShrinksAnArray_AndClearsAReference()
        {
            tool.Create(TypeName, Asset, new[] { "names.Array.size=4" });

            tool.Set(Asset, new[] { "names.Array.size=1", "link=null" });

            var data = Load();
            Assert.AreEqual(1, data.Names.Length);
            Assert.IsNull(data.Link);
        }

        [Test]
        public void Set_ReportsBadEntriesWithoutStoppingTheOthers()
        {
            tool.Create(TypeName, Asset);

            var result = tool.Set(Asset, new[] { "hidden=oops", "kind=Gamma", "missing=1", "hidden=9" });

            Assert.AreEqual(3, result.Failed);
            Assert.AreEqual(1, result.Changed);
            Assert.AreEqual(9, Load().Hidden);
        }

        [Test]
        public void Set_RefusesPackagesAndMissingAssets()
        {
            Assert.Throws<ArgumentException>(() => tool.Set("Packages/com.feeder.mcp/package.json", new[] { "a=1" }));
            Assert.Throws<ArgumentException>(() => tool.Set(Root + "/None.asset", new[] { "a=1" }));
        }

        [Test]
        public void Get_ListsPrivateFieldsAndArraySizes()
        {
            tool.Create(TypeName, Asset, new[] { "hidden=4", "names.Array.size=2" });

            var all = tool.Get(Asset);
            var hidden = all.Properties.Single(p => p.Path == "hidden");
            Assert.AreEqual("4", hidden.Value);
            Assert.AreEqual("Integer", hidden.Type);
            Assert.AreEqual("[2]", all.Properties.Single(p => p.Path == "names").Value);

            var branch = tool.Get(Asset, "names", 2);
            Assert.IsTrue(branch.Properties.Any(p => p.Path == "names.Array.size" && p.Value == "2"));
            Assert.IsFalse(all.Properties.Any(p => p.Path == "m_Script"));
        }
    }
}
