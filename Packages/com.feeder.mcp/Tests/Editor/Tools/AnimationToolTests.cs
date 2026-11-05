#nullable enable

using System;
using System.Linq;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class AnimationToolTests
    {
        const string Folder = "Assets/_MxTestAnim";

        [SetUp]
        [TearDown]
        public void Clean()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
        }

        static CurveKey Key(float t, float v) => new CurveKey { Time = t, Value = v };

        static AnimatorControllerParameter P(string name, AnimatorControllerParameterType type)
            => new AnimatorControllerParameter { name = name, type = type };

        [Test]
        public void CheckAssetPath_AcceptsAssetsAndRejectsOthers()
        {
            Assert.AreEqual("Assets/A/B.anim", AnimationEdit.CheckAssetPath("Assets\\A\\B.anim", ".anim"));
            Assert.Throws<ArgumentException>(() => AnimationEdit.CheckAssetPath("Packages/x/B.anim", ".anim"));
            Assert.Throws<ArgumentException>(() => AnimationEdit.CheckAssetPath("Assets/B.controller", ".anim"));
            Assert.Throws<ArgumentException>(() => AnimationEdit.CheckAssetPath("Assets/../B.anim", ".anim"));
        }

        [Test]
        public void BuildCurve_SortsKeys_AndRejectsBadInput()
        {
            var curve = AnimationEdit.BuildCurve(new[] { Key(1, 5), Key(0, 0) }, null);
            Assert.AreEqual(2, curve.length);
            Assert.AreEqual(0f, curve[0].time);
            Assert.Throws<ArgumentException>(() => AnimationEdit.BuildCurve(null, null));
            Assert.Throws<ArgumentException>(() => AnimationEdit.BuildCurve(new[] { Key(0, 0), Key(0, 1) }, null));
            Assert.Throws<ArgumentException>(() => AnimationEdit.BuildCurve(new[] { Key(-1, 0) }, null));
            Assert.Throws<ArgumentException>(() => AnimationEdit.BuildCurve(new[] { Key(0, float.NaN) }, null));
            Assert.Throws<ArgumentException>(() => AnimationEdit.BuildCurve(new[] { Key(0, 0) }, "cubic"));
        }

        [Test]
        public void BuildCurve_LinearAndConstantTangents()
        {
            var linear = AnimationEdit.BuildCurve(new[] { Key(0, 0), Key(2, 4) }, "linear");
            Assert.AreEqual(2f, linear[0].outTangent, 1e-4f);
            Assert.AreEqual(2f, linear[1].inTangent, 1e-4f);
            var constant = AnimationEdit.BuildCurve(new[] { Key(0, 0), Key(1, 1) }, "constant");
            Assert.IsTrue(float.IsPositiveInfinity(constant[0].outTangent));
        }

        [Test]
        public void ResolveType_FindsTransform_AndRejectsUnknown()
        {
            Assert.AreEqual(typeof(Transform), AnimationEdit.ResolveType("transform"));
            Assert.Throws<ArgumentException>(() => AnimationEdit.ResolveType("NoSuchTypeAnywhere"));
            Assert.Throws<ArgumentException>(() => AnimationEdit.ResolveType(" "));
        }

        [Test]
        public void PropertyWarning_FlagsOnlyUnknownProperties()
        {
            Assert.IsNull(AnimationEdit.PropertyWarning(typeof(Transform), "m_LocalPosition.x"));
            Assert.IsNull(AnimationEdit.PropertyWarning(typeof(Transform), "localEulerAnglesRaw.y"));
            Assert.IsNull(AnimationEdit.PropertyWarning(typeof(Light), "m_Intensity"));
            Assert.IsNull(AnimationEdit.PropertyWarning(typeof(Behaviour), "m_Enabled"));
            StringAssert.StartsWith("warning:", AnimationEdit.PropertyWarning(typeof(Transform), "nope.x"));
        }

        [Test]
        public void ParseCondition_ByParameterType()
        {
            var ps = new[]
            {
                P("Speed", AnimatorControllerParameterType.Float), P("Mode", AnimatorControllerParameterType.Int),
                P("Grounded", AnimatorControllerParameterType.Bool), P("Jump", AnimatorControllerParameterType.Trigger),
            };
            Assert.AreEqual(("Speed", AnimatorConditionMode.Greater, 0.5f), AnimationEdit.ParseCondition("Speed > 0.5", ps));
            Assert.AreEqual(("Mode", AnimatorConditionMode.NotEqual, 2f), AnimationEdit.ParseCondition("Mode != 2", ps));
            Assert.AreEqual(("Mode", AnimatorConditionMode.Equals, 2f), AnimationEdit.ParseCondition("Mode==2", ps));
            Assert.AreEqual(("Grounded", AnimatorConditionMode.IfNot, 0f), AnimationEdit.ParseCondition("!Grounded", ps));
            Assert.AreEqual(("Jump", AnimatorConditionMode.If, 0f), AnimationEdit.ParseCondition("Jump", ps));
            Assert.Throws<ArgumentException>(() => AnimationEdit.ParseCondition("Speed == 1", ps));
            Assert.Throws<ArgumentException>(() => AnimationEdit.ParseCondition("Grounded > 1", ps));
            Assert.Throws<ArgumentException>(() => AnimationEdit.ParseCondition("!Speed > 1", ps));
            Assert.Throws<ArgumentException>(() => AnimationEdit.ParseCondition("Nope", ps));
            Assert.Throws<ArgumentException>(() => AnimationEdit.ParseCondition("Speed > x", ps));
        }

        [Test]
        public void Clip_CreateSetCurveEventsAndDescribe()
        {
            var path = Folder + "/Spin.anim";
            var clip = AnimationEdit.CreateClip(path, 30, true);
            Assert.Throws<InvalidOperationException>(() => AnimationEdit.CreateClip(path, 30, true));

            var count = AnimationEdit.SetCurve(clip, "Arm", "Transform", "localEulerAnglesRaw.y", new[] { Key(0, 0), Key(1, 360) }, "linear", false);
            Assert.AreEqual(2, count);
            AnimationEdit.SetEvents(clip, new[] { new ClipEvent { Time = 0.5f, FunctionName = "Hit", IntParameter = 3 } });

            var text = AnimationEdit.DescribeClip(clip);
            StringAssert.Contains("loop=True", text);
            StringAssert.Contains("path='Arm' type=Transform property=localEulerAnglesRaw.y keys=2", text);
            StringAssert.Contains("Hit(float=0 int=3", text);

            AnimationEdit.SetCurve(clip, "Arm", "Transform", "localEulerAnglesRaw.y", null, null, true);
            Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length);
        }

        [Test]
        public void Controller_BuildsParametersStatesTransitionsAndBlendTree()
        {
            AnimationEdit.CreateClip(Folder + "/Idle.anim", 30, false);
            var controller = AnimationEdit.CreateController(Folder + "/Player.controller");

            var result = AnimationEdit.ApplyOps(controller, new[]
            {
                new ControllerOp { Op = "add-parameter", Name = "Speed", Type = "Float" },
                new ControllerOp { Op = "add-parameter", Name = "Jump", Type = "Trigger" },
                new ControllerOp { Op = "add-state", Name = "Idle", Motion = Folder + "/Idle.anim" },
                new ControllerOp { Op = "add-state", Name = "Run", Speed = 1.5f },
                new ControllerOp { Op = "set-default-state", Name = "Idle" },
                new ControllerOp { Op = "add-transition", From = "Idle", To = "Run", Conditions = new[] { "Speed > 0.5" } },
                new ControllerOp { Op = "add-transition", From = "Any", To = "Idle", Conditions = new[] { "Jump" } },
                new ControllerOp
                {
                    Op = "add-blend-tree", Name = "Move", BlendParameter = "Speed",
                    Children = new[] { new BlendChild { Motion = Folder + "/Idle.anim", Threshold = 0 } },
                },
                new ControllerOp { Op = "add-layer", Layer = "Upper", Weight = 0.5f },
            });
            StringAssert.Contains("Applied 9", result);

            var text = AnimationEdit.DescribeController(controller);
            StringAssert.Contains("- Speed Float", text);
            StringAssert.Contains("default_state=Idle", text);
            StringAssert.Contains("-> Run exit_time=off", text);
            StringAssert.Contains("[Speed > 0.5]", text);
            StringAssert.Contains("Any -> Idle", text);
            StringAssert.Contains("BlendTree(Simple1D, Speed, 1 children)", text);
            StringAssert.Contains("layer 'Upper' weight=0.5", text);
        }

        [Test]
        public void Controller_StopsAtFirstBadOperationAndSaysWhere()
        {
            var controller = AnimationEdit.CreateController(Folder + "/Bad.controller");
            var ex = Assert.Throws<ArgumentException>(() => AnimationEdit.ApplyOps(controller, new[]
            {
                new ControllerOp { Op = "add-parameter", Name = "A", Type = "Bool" },
                new ControllerOp { Op = "add-transition", From = "X", To = "Y" },
            }));
            StringAssert.Contains("Applied 1 of 2", ex!.Message);
            Assert.AreEqual(1, controller.parameters.Length);
            Assert.Throws<ArgumentException>(() => AnimationEdit.ApplyOps(controller, new[] { new ControllerOp { Op = "explode" } }));
            Assert.Throws<ArgumentException>(() => AnimationEdit.ApplyOps(controller, Array.Empty<ControllerOp>()));
            Assert.IsTrue(controller.layers.All(l => l.stateMachine != null));
        }
    }
}
