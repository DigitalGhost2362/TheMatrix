#nullable enable

using System;
using System.Linq;
using Feeder.MCP.Editor.API;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class PhysicsToolTests
    {
        readonly Tool_Physics tool = new Tool_Physics();
        Scene previousActive;
        Scene tempScene;
        GameObject floor = null!;
        readonly System.Collections.Generic.List<GameObject> created = new System.Collections.Generic.List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            Assume.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
            Assume.That(Physics.gravity.y, Is.LessThan(0f));
            previousActive = SceneManager.GetActiveScene();
            tempScene = default;
            if (!string.IsNullOrEmpty(previousActive.path))
            {
                tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(tempScene);
            }

            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "MxFloor";
            floor.transform.position = new Vector3(0, 0, 5000);
            floor.transform.localScale = new Vector3(20, 1, 20);
            created.Add(floor);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created)
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            created.Clear();
            if (previousActive.IsValid())
                SceneManager.SetActiveScene(previousActive);
            if (tempScene.IsValid())
                EditorSceneManager.CloseScene(tempScene, true);
        }

        static float[] Point(float x, float y, float z) => new[] { x, y, z };

        [Test]
        public void Raycast_HitsTheFloorTopWithDistanceAndNormal()
        {
            var result = tool.Query("raycast", Point(0, 5, 5000), Point(0, -1, 0));

            Assert.AreEqual(1, result.Count);
            var hit = result.Hits[0];
            Assert.AreEqual("MxFloor", hit.Name);
            Assert.AreEqual(4.5f, hit.Distance!.Value, 1e-3f);
            Assert.AreEqual(1f, hit.Normal![1], 1e-3f);
            Assert.AreEqual("Default", hit.Layer);
        }

        [Test]
        public void Raycast_MissesWhenPointingAway_AndRespectsMaxDistance()
        {
            Assert.AreEqual(0, tool.Query("raycast", Point(0, 5, 5000), Point(0, 1, 0)).Count);
            Assert.AreEqual(0, tool.Query("raycast", Point(0, 5, 5000), Point(0, -1, 0), maxDistance: 2f).Count);
        }

        [Test]
        public void Raycast_LayerFilterAndTriggers()
        {
            var ignored = tool.Query("raycast", Point(0, 5, 5000), Point(0, -1, 0), layers: new[] { "Water" });
            Assert.AreEqual(0, ignored.Count);

            floor.GetComponent<Collider>().isTrigger = true;
            Assert.AreEqual(0, tool.Query("raycast", Point(0, 5, 5000), Point(0, -1, 0)).Count);
            Assert.AreEqual(1, tool.Query("raycast", Point(0, 5, 5000), Point(0, -1, 0), includeTriggers: true).Count);
        }

        [Test]
        public void RaycastAll_ReturnsNearestFirst_AndLimitTruncates()
        {
            var upper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            upper.name = "MxUpper";
            created.Add(upper);
            upper.transform.position = new Vector3(0, 2, 5000);

            var all = tool.Query("raycast-all", Point(0, 5, 5000), Point(0, -1, 0));
            CollectionAssert.AreEqual(new[] { "MxUpper", "MxFloor" }, all.Hits.Select(h => h.Name));

            var one = tool.Query("raycast-all", Point(0, 5, 5000), Point(0, -1, 0), limit: 1);
            Assert.IsTrue(one.Truncated);
            Assert.AreEqual(2, one.Count);
            Assert.AreEqual(1, one.Hits.Count);
        }

        [Test]
        public void SphereCastAndBoxCast_HitTheFloor()
        {
            Assert.AreEqual("MxFloor", tool.Query("spherecast", Point(0, 5, 5000), Point(0, -1, 0), radius: 0.5f).Hits[0].Name);
            Assert.AreEqual("MxFloor", tool.Query("boxcast", Point(0, 5, 5000), Point(0, -1, 0), size: Point(1, 1, 1)).Hits[0].Name);
        }

        [Test]
        public void Overlaps_FindTouchingColliders()
        {
            var inside = tool.Query("overlap-sphere", Point(0, 0.6f, 5000), radius: 0.5f);
            Assert.AreEqual("MxFloor", inside.Hits.Single().Name);
            Assert.IsNull(inside.Hits[0].Distance);

            Assert.AreEqual(0, tool.Query("overlap-sphere", Point(0, 3, 5000), radius: 0.5f).Count);
            Assert.AreEqual(1, tool.Query("overlap-box", Point(0, 0.5f, 5000), size: Point(1, 1, 1), rotation: Point(0, 45, 0)).Count);
        }

        [Test]
        public void Query_ValidatesInput()
        {
            Assert.Throws<ArgumentException>(() => tool.Query("teleport", Point(0, 0, 0)));
            Assert.Throws<ArgumentException>(() => tool.Query("raycast", Point(0, 0, 0)));
            Assert.Throws<ArgumentException>(() => tool.Query("raycast", Point(0, 0, 0), Point(0, 0, 0)));
            Assert.Throws<ArgumentException>(() => tool.Query("raycast", new[] { 1f, 2f }, Point(0, 1, 0)));
            Assert.Throws<ArgumentException>(() => tool.Query("raycast", Point(0, 0, 0), Point(0, 1, 0), layers: new[] { "NoSuchLayer" }));
        }

        GameObject Ball()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "MxBall";
            created.Add(ball);
            ball.transform.position = new Vector3(0, 5, 5000);
            ball.AddComponent<Rigidbody>();
            return ball;
        }

        [Test]
        public void Simulate_ReportsTheFall_AndLeavesTheSceneUnchanged()
        {
            var ball = Ball();
            var rotation = ball.transform.rotation;

            var result = tool.Simulate(30);

            Assert.IsFalse(result.Applied);
            var moved = result.Moved.Single(m => m.Path == "MxBall");
            Assert.Greater(moved.Moved, 1f);
            Assert.Less(moved.To[1], moved.From[1]);
            Assert.AreEqual(new Vector3(0, 5, 5000), ball.transform.position);
            Assert.AreEqual(rotation, ball.transform.rotation);
#if UNITY_6000_0_OR_NEWER
            Assert.AreEqual(Vector3.zero, ball.GetComponent<Rigidbody>().linearVelocity);
#else
            Assert.AreEqual(Vector3.zero, ball.GetComponent<Rigidbody>().velocity);
#endif
            Assert.AreEqual(SimulationMode.FixedUpdate, Physics.simulationMode);
        }

        [Test]
        public void Simulate_BallRestsOnTheFloor()
        {
            var ball = Ball();

            var result = tool.Simulate(200);

            Assert.AreEqual(1.0f, result.Moved.Single(m => m.Path == "MxBall").To[1], 0.1f);
            Assert.AreEqual(5f, ball.transform.position.y, 1e-4f);
        }

        [Test]
        public void Simulate_ApplyKeepsTheResultAsOneUndoStep()
        {
            var ball = Ball();

            var result = tool.Simulate(30, apply: true);

            Assert.IsTrue(result.Applied);
            Assert.Less(ball.transform.position.y, 5f);
            Assert.AreEqual("Matrix physics simulate", Undo.GetCurrentGroupName());
        }

        [Test]
        public void Simulate_ValidatesInput()
        {
            Assert.Throws<ArgumentException>(() => tool.Simulate(0));
            Assert.Throws<ArgumentException>(() => tool.Simulate(5000));
            Assert.Throws<ArgumentException>(() => tool.Simulate(1, deltaTime: 0f));
        }
    }
}
