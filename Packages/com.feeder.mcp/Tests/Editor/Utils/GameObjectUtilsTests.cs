#nullable enable

using System;
using System.Linq;
using Feeder.MCP.Runtime.Utils;
using NUnit.Framework;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class GameObjectUtilsTests
    {
        GameObject _root = null!;
        string _rootName = null!;

        [SetUp]
        public void CreateRoot()
        {
            _rootName = "MxTest_Utils_" + Guid.NewGuid().ToString("N");
            _root = new GameObject(_rootName);
        }

        [TearDown]
        public void DestroyRoot()
        {
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void AddChild_ParentsWithoutMovingTheLocalPose()
        {
            _root.transform.position = new Vector3(10, 0, 0);

            var child = _root.AddChild("Arm")!;

            Assert.AreSame(_root.transform, child.transform.parent);
            Assert.AreEqual(Vector3.zero, child.transform.localPosition);
            Assert.AreEqual(new Vector3(10, 0, 0), child.transform.position);
        }

        [Test]
        public void AddChild_RejectsEmptyNames()
        {
            Assert.IsNull(_root.AddChild(string.Empty));
            Assert.AreEqual(0, _root.transform.childCount);
        }

        [Test]
        public void GetPath_JoinsAncestorNames()
        {
            var hand = _root.AddChild("Arm")!.AddChild("Hand")!;

            Assert.AreEqual($"{_rootName}/Arm/Hand", hand.GetPath());
            Assert.AreEqual(_rootName, _root.GetPath());
            Assert.IsNull(((GameObject?)null).GetPath());
        }

        [Test]
        public void FindChildByName_LooksOnlyOneLevelDown()
        {
            var arm = _root.AddChild("Arm")!;
            arm.AddChild("Hand");

            Assert.AreSame(arm, _root.FindChildByName("Arm"));
            Assert.IsNull(_root.FindChildByName("Hand"));
        }

        [Test]
        public void FindByPath_WithRoot_IsRelativeToIt()
        {
            var hand = _root.AddChild("Arm")!.AddChild("Hand")!;

            Assert.AreSame(hand, GameObjectUtils.FindByPath("Arm/Hand", _root));
            Assert.IsNull(GameObjectUtils.FindByPath("Hand", _root));
        }

        [Test]
        public void FindByPath_WithoutRoot_StartsAtSceneRoots()
        {
            var hand = _root.AddChild("Arm")!.AddChild("Hand")!;

            Assert.AreSame(hand, GameObjectUtils.FindByPath($"{_rootName}/Arm/Hand"));
            Assert.IsNull(GameObjectUtils.FindByPath(string.Empty));
        }

        [Test]
        public void FindByName_WithRoot_SearchesDepthFirstIncludingTheRoot()
        {
            var hand = _root.AddChild("Arm")!.AddChild("Hand")!;

            Assert.AreSame(hand, GameObjectUtils.FindByName("Hand", _root));
            Assert.AreSame(_root, GameObjectUtils.FindByName(_rootName, _root));
            Assert.IsNull(GameObjectUtils.FindByName("Leg", _root));
        }

        [Test]
        public void GetAllRecursively_YieldsEveryNodeWithItsPath()
        {
            var arm = _root.AddChild("Arm")!;
            arm.AddChild("Hand");
            _root.AddChild("Head");

            var paths = _root.GetAllRecursively().Select(p => p.Key).ToArray();

            CollectionAssert.AreEqual(
                new[] { _rootName, $"{_rootName}/Arm", $"{_rootName}/Arm/Hand", $"{_rootName}/Head" },
                paths);
        }

        [Test]
        public void SetTransform_WorldAndLocalSpace()
        {
            var child = _root.AddChild("Arm")!;
            _root.transform.position = new Vector3(0, 5, 0);

            child.SetTransform(position: new Vector3(1, 1, 1), isLocalSpace: true);
            Assert.AreEqual(new Vector3(1, 1, 1), child.transform.localPosition);

            child.SetTransform(position: new Vector3(1, 1, 1), scale: new Vector3(2, 2, 2));
            Assert.AreEqual(new Vector3(1, 1, 1), child.transform.position);
            Assert.AreEqual(new Vector3(1, -4, 1), child.transform.localPosition);
            Assert.AreEqual(new Vector3(2, 2, 2), child.transform.localScale);
        }

        [Test]
        public void CalculateBounds_WithoutRenderers_IsAPointAtThePosition()
        {
            _root.transform.position = new Vector3(2, 3, 4);

            var bounds = _root.CalculateBounds();

            Assert.AreEqual(new Vector3(2, 3, 4), bounds.center);
            Assert.AreEqual(Vector3.zero, bounds.size);
        }
    }
}
