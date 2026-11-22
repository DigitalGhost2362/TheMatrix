#nullable enable

using System;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.API.BuildRunner;
using NUnit.Framework;
using UnityEditor;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class BuildJobTests
    {
        string _dir = string.Empty;
        string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "feeder_build_" + Guid.NewGuid().ToString("N"));
            _root = Path.Combine(_dir, "Project");
            BuildJobStore.PathOverride = Path.Combine(_dir, "build.json");
        }

        [TearDown]
        public void TearDown()
        {
            BuildJobStore.PathOverride = null;
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        BuildPlan Validate(string[]? scenes = null, string? output = null, string? target = null, string[]? defaults = null)
            => BuildRequest.Validate(scenes, output, target, false, _root, BuildTarget.StandaloneWindows64, _ => true, defaults ?? Array.Empty<string>(), "My Game");

        [Test]
        public void Validate_DefaultsToActiveTargetAndBuildsFolder()
        {
            var plan = Validate(new[] { "Assets/A.unity" });
            Assert.AreEqual(BuildTarget.StandaloneWindows64, plan.Target);
            StringAssert.EndsWith("/Project/Builds/StandaloneWindows64/My_Game.exe", plan.OutputPath);
        }

        [Test]
        public void Validate_UsesBuildSettingsScenesWhenNoneGiven()
        {
            var plan = Validate(null, null, null, new[] { "Assets/B.unity" });
            CollectionAssert.AreEqual(new[] { "Assets/B.unity" }, plan.Scenes);
        }

        [Test]
        public void Validate_NoScenesAtAll_Throws()
            => Assert.Throws<ArgumentException>(() => Validate());

        [Test]
        public void Validate_DuplicateScenesAreMerged_AndSlashesFixed()
        {
            var plan = Validate(new[] { "Assets\\A.unity", "Assets/A.unity", " Assets/B.unity " });
            CollectionAssert.AreEqual(new[] { "Assets/A.unity", "Assets/B.unity" }, plan.Scenes);
        }

        [TestCase("Scenes/A.unity")]
        [TestCase("Assets/A.prefab")]
        [TestCase("Assets/../A.unity")]
        [TestCase("")]
        public void Validate_BadScenePath_Throws(string scene)
            => Assert.Throws<ArgumentException>(() => Validate(new[] { scene }));

        [Test]
        public void Validate_MissingScene_NamesIt()
        {
            var ex = Assert.Throws<ArgumentException>(() => BuildRequest.Validate(
                new[] { "Assets/Gone.unity" }, null, null, false, _root, BuildTarget.StandaloneWindows64, _ => false, Array.Empty<string>(), "G"));
            StringAssert.Contains("Assets/Gone.unity", ex!.Message);
        }

        [TestCase("Assets/Out")]
        [TestCase("Assets")]
        [TestCase("Packages/x")]
        [TestCase("ProjectSettings/x")]
        [TestCase("Library/x")]
        [TestCase("assets/out")]
        [TestCase("Builds/../Assets/x")]
        [TestCase(".")]
        public void Validate_OutputInsideProject_Throws(string output)
            => Assert.Throws<ArgumentException>(() => Validate(new[] { "Assets/A.unity" }, output));

        [Test]
        public void Validate_OutputUnderTempIsAllowed_AndExtensionAdded()
        {
            var plan = Validate(new[] { "Assets/A.unity" }, "Temp/out/game");
            StringAssert.EndsWith("/Project/Temp/out/game.exe", plan.OutputPath);
        }

        [Test]
        public void Validate_AbsoluteOutsideProjectIsAllowed()
        {
            var outside = Path.Combine(_dir, "elsewhere", "g.exe");
            var plan = Validate(new[] { "Assets/A.unity" }, outside);
            Assert.AreEqual(outside.Replace('\\', '/'), plan.OutputPath);
        }

        [Test]
        public void ParseTarget_NamesAliasesAndDefault()
        {
            Assert.AreEqual(BuildTarget.Android, BuildRequest.ParseTarget("android", BuildTarget.WebGL));
            Assert.AreEqual(BuildTarget.StandaloneLinux64, BuildRequest.ParseTarget("Linux", BuildTarget.WebGL));
            Assert.AreEqual(BuildTarget.WebGL, BuildRequest.ParseTarget("  ", BuildTarget.WebGL));
        }

        [TestCase("NoTarget")]
        [TestCase("5")]
        [TestCase("Amiga")]
        public void ParseTarget_Bad_Throws(string target)
            => Assert.Throws<ArgumentException>(() => BuildRequest.ParseTarget(target, BuildTarget.WebGL));

        [Test]
        public void Store_RoundTripsAndFollowsLifecycle()
        {
            Assert.IsNull(BuildJobStore.Read());
            var plan = Validate(new[] { "Assets/A.unity" });
            var record = BuildJobStore.Begin(plan);
            Assert.AreEqual(BuildJobStore.Queued, BuildJobStore.Read()!.state);

            BuildJobStore.MarkRunning(record.jobId);
            Assert.AreEqual(BuildJobStore.Running, BuildJobStore.Read()!.state);

            var running = BuildJobStore.Read()!;
            running.result = "Succeeded";
            running.messages.Add("boom");
            BuildJobStore.Complete(running, BuildJobStore.Succeeded, string.Empty);
            var back = BuildJobStore.Read()!;
            Assert.IsTrue(back.IsTerminal);
            Assert.AreEqual("boom", back.messages.Single());
            Assert.AreEqual(plan.OutputPath, back.outputPath);

            BuildJobStore.MarkRunning(record.jobId);
            Assert.AreEqual(BuildJobStore.Succeeded, BuildJobStore.Read()!.state, "a finished job does not go back to running");
        }

        [Test]
        public void Store_CorruptFile_ReadsAsNull()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(BuildJobStore.PathOverride!, "{ nope");
            Assert.IsNull(BuildJobStore.Read());
        }

        [Test]
        public void Cancel_QueuedJobEndsAtOnce_FinishedJobIsRefused()
        {
            var record = BuildJobStore.Begin(Validate(new[] { "Assets/A.unity" }));
            Assert.IsTrue(BuildJobStore.RequestCancel(record.jobId));
            Assert.AreEqual(BuildJobStore.Cancelled, BuildJobStore.Read()!.state);
            Assert.IsFalse(BuildJobStore.RequestCancel(record.jobId));
        }

        [Test]
        public void Cancel_RunningJobOnlyRaisesTheFlag()
        {
            var record = BuildJobStore.Begin(Validate(new[] { "Assets/A.unity" }));
            BuildJobStore.MarkRunning(record.jobId);
            Assert.IsTrue(BuildJobStore.RequestCancel(record.jobId));
            var back = BuildJobStore.Read()!;
            Assert.AreEqual(BuildJobStore.Running, back.state);
            Assert.IsTrue(back.cancelRequested);
        }

        [Test]
        public void Format_NoRecordAndUnknownJob()
        {
            StringAssert.StartsWith("[no_build]", BuildStatusFormat.Format(null, null, 0));
            var record = BuildJobStore.Begin(Validate(new[] { "Assets/A.unity" }));
            StringAssert.StartsWith("[unknown_job]", BuildStatusFormat.Format(record, "nope", DateTime.UtcNow.Ticks));
        }

        [Test]
        public void Format_ActiveJobCarriesRetryAfter()
        {
            var record = BuildJobStore.Begin(Validate(new[] { "Assets/A.unity" }));
            var text = BuildStatusFormat.Format(record, record.jobId, record.startedTicks + TimeSpan.FromSeconds(7).Ticks);
            StringAssert.Contains("state=queued", text);
            StringAssert.Contains("elapsed_s=7", text);
            StringAssert.Contains($"retry_after_ms={BuildJobStore.RetryAfterMs}", text);
        }

        [Test]
        public void Format_FailedJobListsErrors()
        {
            var record = BuildJobStore.Begin(Validate(new[] { "Assets/A.unity" }));
            record.result = "Failed";
            record.totalErrors = 2;
            record.messages.Add("CS0103: name does not exist");
            BuildJobStore.Complete(record, BuildJobStore.Failed, "Build Failed.");
            var text = BuildStatusFormat.Format(BuildJobStore.Read(), null, DateTime.UtcNow.Ticks);
            StringAssert.Contains("state=failed", text);
            StringAssert.Contains("errors=2", text);
            StringAssert.Contains("error: Build Failed.", text);
            StringAssert.Contains("- CS0103", text);
            StringAssert.DoesNotContain("retry_after_ms", text);
        }

        [Test]
        public void Format_SucceededJobShowsSummary()
        {
            var record = BuildJobStore.Begin(Validate(new[] { "Assets/A.unity" }));
            record.result = "Succeeded";
            record.totalSizeBytes = 1234;
            BuildJobStore.Complete(record, BuildJobStore.Succeeded, string.Empty);
            var text = BuildStatusFormat.Format(BuildJobStore.Read(), null, DateTime.UtcNow.Ticks);
            StringAssert.Contains("summary: result=Succeeded errors=0 warnings=0 size_bytes=1234", text);
            StringAssert.Contains("scenes: Assets/A.unity", text);
        }
    }
}
