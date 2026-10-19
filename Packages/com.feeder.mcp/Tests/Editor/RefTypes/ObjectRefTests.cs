#nullable enable
#pragma warning disable CS0618

using System;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.RefTypes
{
    public sealed class ObjectRefTests
    {
        const string ScriptAssetPath = "Packages/com.feeder.mcp/Editor/Scripts/MatrixActivation.cs";

        GameObject? _go;

        [TearDown]
        public void DestroyObjects()
        {
            if (_go != null)
                UnityEngine.Object.DestroyImmediate(_go);
        }

        MonoScript LoadScript()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptAssetPath);
            Assert.IsNotNull(script, ScriptAssetPath);
            return script;
        }

        [Test]
        public void ObjectRef_Default_IsInvalid()
        {
            Assert.IsFalse(new ObjectRef().IsValid(out var error));
            StringAssert.Contains("InstanceID", error);
        }

        [Test]
        public void ObjectRef_FromObject_FindsTheSameInstance()
        {
            _go = new GameObject("MxTest_ObjectRef");
            var reference = _go.ToObjectRef()!;

            Assert.IsTrue(reference.IsValid());
            Assert.AreSame(_go, reference.FindObject());
        }

        [Test]
        public void ObjectRef_FromNull_HasZeroIdAndFindsNothing()
        {
            var reference = new ObjectRef((UnityEngine.Object?)null);

            Assert.AreEqual(0, reference.InstanceID);
            Assert.IsNull(reference.FindObject());
            Assert.IsNull(((ObjectRef?)null).FindObject());
        }

        [Test]
        public void ObjectRef_FindsComponentsToo()
        {
            _go = new GameObject("MxTest_ObjectRef");
            var light = _go.AddComponent<Light>();

            Assert.AreSame(light, new ObjectRef(light.GetInstanceID()).FindObject());
        }

        [Test]
        public void AssetObjectRef_FromAsset_FillsPathGuidAndType()
        {
            var script = LoadScript();
            var reference = new AssetObjectRef(script);

            Assert.AreEqual(script.GetInstanceID(), reference.InstanceID);
            Assert.AreEqual(ScriptAssetPath, reference.AssetPath);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(ScriptAssetPath), reference.AssetGuid);
            Assert.AreEqual(typeof(MonoScript), reference.AssetType);
        }

        [Test]
        public void AssetObjectRef_FromPath_LoadsTheMainAsset()
        {
            Assert.AreEqual(LoadScript().GetInstanceID(), new AssetObjectRef(ScriptAssetPath).InstanceID);
        }

        [Test]
        public void AssetObjectRef_FromMissingPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => new AssetObjectRef("Assets/MxTest_DoesNotExist_9f1c.asset"));
        }

        [Test]
        public void AssetObjectRef_FromSceneObject_ThrowsUnlessAllowed()
        {
            _go = new GameObject("MxTest_NotAnAsset");

            Assert.Throws<ArgumentException>(() => new AssetObjectRef(_go));

            var lenient = new AssetObjectRef(_go, throwIfNotAnAsset: false);
            Assert.AreEqual(_go.GetInstanceID(), lenient.InstanceID);
            Assert.IsNull(lenient.AssetPath);
        }

        [Test]
        public void FindAssetObject_ByPathOnly()
        {
            Assert.AreEqual(LoadScript(), new AssetObjectRef { AssetPath = ScriptAssetPath }.FindAssetObject());
        }

        [Test]
        public void FindAssetObject_ByGuidOnly()
        {
            var guid = AssetDatabase.AssetPathToGUID(ScriptAssetPath);

            Assert.AreEqual(LoadScript(), new AssetObjectRef { AssetGuid = guid }.FindAssetObject<MonoScript>());
        }

        [Test]
        public void FindAssetObject_ByInstanceId_RespectsTheRequestedType()
        {
            var reference = new AssetObjectRef(LoadScript().GetInstanceID());

            Assert.IsNotNull(reference.FindAssetObject<MonoScript>());
            Assert.IsNull(reference.FindAssetObject<Material>());
        }

        [TestCase("Assets/Materials/Rock.mat", true)]
        [TestCase("Packages/com.example/Icon.png", true)]
        [TestCase("Library/Artifacts/x", false)]
        [TestCase("C:/Temp/thing.asset", false)]
        public void AssetObjectRef_IsValid_RequiresAProjectRootedPath(string path, bool expected)
        {
            Assert.AreEqual(expected, new AssetObjectRef { AssetPath = path }.IsValid());
        }

        [Test]
        public void AssetObjectRef_GuidAloneIsValid_NothingIsInvalid()
        {
            Assert.IsTrue(new AssetObjectRef { AssetGuid = "0123456789abcdef0123456789abcdef" }.IsValid());
            Assert.IsFalse(new AssetObjectRef().IsValid(out var error));
            StringAssert.Contains("assetPath", error);
        }

        [Test]
        public void ToAssetObjectRef_Null_IsAnEmptyReference()
        {
            var reference = ((UnityEngine.Object?)null).ToAssetObjectRef()!;

            Assert.AreEqual(0, reference.InstanceID);
            Assert.IsFalse(reference.IsValid());
        }

        [Test]
        public void IsAsset_DistinguishesAssetsFromSceneObjects()
        {
            _go = new GameObject("MxTest_NotAnAsset");

            Assert.IsTrue(LoadScript().IsAsset());
            Assert.IsFalse(_go.IsAsset());
            Assert.AreEqual(ScriptAssetPath, LoadScript().GetAssetPath());
            Assert.IsNull(_go.GetAssetPath());
        }
    }
}
