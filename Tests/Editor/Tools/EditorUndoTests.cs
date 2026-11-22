#nullable enable

using System;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class EditorUndoTests
    {
        readonly Tool_Editor tool = new Tool_Editor();
        GameObject? created;

        [TearDown]
        public void TearDown()
        {
            if (created != null)
                UnityEngine.Object.DestroyImmediate(created);
            created = null;
        }

        GameObject CreateWithUndo(string undoName)
        {
            Undo.IncrementCurrentGroup();
            var go = new GameObject("MatrixUndoToolTest");
            Undo.RegisterCreatedObjectUndo(go, undoName);
            Undo.SetCurrentGroupName(undoName);
            Undo.IncrementCurrentGroup();
            return go;
        }

        [Test]
        public void Undo_ReturnsGroupName_AndRevertsChange()
        {
            created = CreateWithUndo("Matrix undo tool test");

            var result = tool.UndoRedo("undo");

            Assert.AreEqual(1, result.Performed);
            Assert.AreEqual("Matrix undo tool test", result.Groups[0]);
            Assert.IsTrue(created == null, "the created object should be gone");
        }

        [Test]
        public void Redo_BringsTheObjectBack_AndNamesTheGroup()
        {
            created = CreateWithUndo("Matrix redo tool test");
            tool.UndoRedo("undo");

            var result = tool.UndoRedo("redo");

            Assert.AreEqual(1, result.Performed);
            Assert.AreEqual("Matrix redo tool test", result.Groups[0]);
            Assert.IsNotNull(GameObject.Find("MatrixUndoToolTest"));
            created = GameObject.Find("MatrixUndoToolTest");
        }

        [Test]
        public void Action_IsCaseInsensitive()
        {
            created = CreateWithUndo("Matrix case test");
            Assert.AreEqual("undo", tool.UndoRedo(" UNDO ").Action);
        }

        [Test]
        public void InvalidAction_Throws()
        {
            Assert.Throws<ArgumentException>(() => tool.UndoRedo("rewind"));
        }

        [TestCase(0)]
        [TestCase(51)]
        public void StepsOutOfRange_Throws(int steps)
        {
            Assert.Throws<ArgumentException>(() => tool.UndoRedo("undo", steps));
        }

        [Test]
        public void Steps_UndoesSeveralGroups_InOrder()
        {
            var first = CreateWithUndo("Matrix steps A");
            var second = CreateWithUndo("Matrix steps B");
            created = null;

            var result = tool.UndoRedo("undo", 2);

            Assert.AreEqual(2, result.Performed);
            Assert.AreEqual("Matrix steps B", result.Groups[0]);
            Assert.AreEqual("Matrix steps A", result.Groups[1]);
            Assert.IsTrue(first == null && second == null);
        }
    }
}
