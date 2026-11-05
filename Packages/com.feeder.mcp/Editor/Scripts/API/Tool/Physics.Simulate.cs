#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Physics
    {
        public const int MaxSimulateSteps = 1000;

        public sealed class SimulatedBody
        {
            public string Path { get; set; } = string.Empty;
            public int InstanceId { get; set; }
            public float[] From { get; set; } = Array.Empty<float>();
            public float[] To { get; set; } = Array.Empty<float>();
            public float Moved { get; set; }
            public float Turned { get; set; }
        }

        public sealed class PhysicsSimulateResult
        {
            public int Steps { get; set; }
            public float DeltaTime { get; set; }
            public int Bodies { get; set; }
            public int MovedBodies { get; set; }
            public bool Applied { get; set; }
            public bool Truncated { get; set; }
            public List<SimulatedBody> Moved { get; set; } = new List<SimulatedBody>();
        }

        sealed class BodyState
        {
            public Rigidbody Body = null!;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 Velocity;
            public Vector3 Angular;
            public Vector3 StartWorld;
            public Quaternion StartWorldRotation;
        }

        [AiTool
        (
            PhysicsSimulateToolId,
            Title = "Physics / Simulate",
            Enabled = false
        )]
        [AiSkillDescription("Run N physics steps on the open scenes in Edit mode and report which Rigidbodies moved, then put everything back (or keep the result with undo).")]
        [AiSkillBody("Simulates physics in Edit mode, so the agent can check a level (does it stand, does it fall) without a Play session.\n\n" +
            "## Inputs\n\n" +
            "- `steps` - how many steps, 1 to 1000. Default: 1.\n" +
            "- `deltaTime` - seconds per step. Default: the project's fixed timestep.\n" +
            "- `apply` - default false: every Rigidbody goes back to its exact position, rotation and velocity after the run, " +
            "so the scene is unchanged. true keeps the simulated positions as one undo step ('Matrix physics simulate') and marks the scene dirty (it does not save it).\n" +
            "- `limit` - maximum moved bodies listed. Default: 50.\n\n" +
            "Refused in Play mode (physics already runs). Only the 3D physics scene is simulated. Colliders without a Rigidbody do not move.")]
        [Description("Simulate N physics steps in Edit mode and list the Rigidbodies that moved (from, to, distance, turn). " +
            "apply=false (default) restores every Rigidbody afterwards so nothing changes; apply=true keeps the result as one undo step. Refused in Play mode.")]
        public PhysicsSimulateResult Simulate
        (
            [Description("Number of steps, 1 to 1000. Default: 1")]
            int? steps = 1,
            [Description("Seconds per step. Default: the project's fixed timestep.")]
            float? deltaTime = null,
            [Description("Keep the simulated result (one undo step). Default: false")]
            bool? apply = false,
            [Description("Maximum moved bodies listed. Default: 50")]
            int? limit = 50
        )
        {
            var count = steps ?? 1;
            if (count < 1 || count > MaxSimulateSteps)
                throw new ArgumentException($"steps must be between 1 and {MaxSimulateSteps}.");
            if (deltaTime.HasValue && (deltaTime.Value <= 0f || float.IsNaN(deltaTime.Value)))
                throw new ArgumentException("deltaTime must be greater than 0.");

            return MainThread.Instance.Run(() =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Play mode is on: physics already runs. Use this tool in Edit mode.");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("The Editor is compiling or importing: try again when it is done.");
                return RunSimulation(count, deltaTime ?? Time.fixedDeltaTime, apply ?? false, Math.Max(1, limit ?? 50));
            });
        }

        /// <summary>Simulates on the main thread; without keep, every Rigidbody is restored.</summary>
        public static PhysicsSimulateResult RunSimulation(int steps, float deltaTime, bool keep, int limit)
        {
            var bodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var states = bodies.Select(b => new BodyState
            {
                Body = b,
                LocalPosition = b.transform.localPosition,
                LocalRotation = b.transform.localRotation,
                Velocity = VelocityOf(b),
                Angular = b.angularVelocity,
                StartWorld = b.transform.position,
                StartWorldRotation = b.transform.rotation,
            }).ToList();

            var result = new PhysicsSimulateResult { Steps = steps, DeltaTime = deltaTime, Bodies = states.Count, Applied = keep };
            if (states.Count == 0)
                return result;

            var group = 0;
            if (keep)
            {
                group = SerializedValues.BeginUndo("Matrix physics simulate");
                Undo.RecordObjects(states.Select(s => (UnityEngine.Object)s.Body.transform).ToArray(), "Matrix physics simulate");
            }

            var mode = Physics.simulationMode;
            var moved = new List<SimulatedBody>();
            try
            {
                Physics.SyncTransforms();
                Physics.simulationMode = SimulationMode.Script;
                for (var i = 0; i < steps; i++)
                    Physics.Simulate(deltaTime);

                foreach (var s in states)
                {
                    var t = s.Body.transform;
                    var distance = Vector3.Distance(s.StartWorld, t.position);
                    var turn = Quaternion.Angle(s.StartWorldRotation, t.rotation);
                    if (distance <= 1e-4f && turn <= 0.01f)
                        continue;
                    moved.Add(new SimulatedBody
                    {
                        Path = PathOf(t),
                        InstanceId = s.Body.gameObject.GetInstanceID(),
                        From = new[] { s.StartWorld.x, s.StartWorld.y, s.StartWorld.z },
                        To = new[] { t.position.x, t.position.y, t.position.z },
                        Moved = distance,
                        Turned = turn,
                    });
                }
            }
            finally
            {
                Physics.simulationMode = mode;
                if (!keep)
                    Restore(states);
                else
                    ResetVelocity(states);
                Physics.SyncTransforms();
                if (keep)
                {
                    SerializedValues.EndUndo(group);
                    foreach (var scene in states.Select(s => s.Body.gameObject.scene).Distinct())
                        EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            result.MovedBodies = moved.Count;
            moved.Sort((a, b) => b.Moved.CompareTo(a.Moved));
            if (moved.Count > limit)
            {
                result.Truncated = true;
                moved.RemoveRange(limit, moved.Count - limit);
            }
            result.Moved = moved;
            return result;
        }

        static void Restore(List<BodyState> states)
        {
            foreach (var s in states)
            {
                if (s.Body == null)
                    continue;
                s.Body.transform.SetLocalPositionAndRotation(s.LocalPosition, s.LocalRotation);
            }
            ResetVelocity(states);
        }

        static void ResetVelocity(List<BodyState> states)
        {
            foreach (var s in states)
            {
                if (s.Body == null || s.Body.isKinematic)
                    continue;
                SetVelocity(s.Body, s.Velocity);
                s.Body.angularVelocity = s.Angular;
            }
        }

        static Vector3 VelocityOf(Rigidbody body)
        {
#if UNITY_6000_0_OR_NEWER
            return body.linearVelocity;
#else
            return body.velocity;
#endif
        }

        static void SetVelocity(Rigidbody body, Vector3 value)
        {
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = value;
#else
            body.velocity = value;
#endif
        }
    }
}
