#nullable enable

using System;
using System.IO;
using System.Reflection;
using Feeder.MCP.Editor.Playtest;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class PlaytestWaitUntilTests
    {
        internal static bool Flag = true;
        static int Count => 3;
        static string Name() => "ready";
        static string Throws() => throw new InvalidOperationException("boom");

        static PlaytestFile Parse(string steps)
        {
            var path = Path.Combine(Path.GetTempPath(), "MxPlaytestWaitUntil_" + Guid.NewGuid().ToString("N") + ".md");
            File.WriteAllText(path, "## Flow: probe\n" + steps);
            try
            {
                return PlaytestFile.Load(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestCase("- wait-until Foo.Bar true", 1)]
        [TestCase("- wait-until Foo.Bar true 5", 1)]
        public void WaitUntil_WithTwoOrThreeArguments_Parses(string line, int steps)
        {
            var file = Parse(line);
            CollectionAssert.IsEmpty(file.Errors);
            Assert.AreEqual(steps, file.Flows["probe"].Count);
            Assert.AreEqual("wait-until", file.Flows["probe"][0].Verb);
        }

        [TestCase("- wait-until Foo.Bar", 1)]
        [TestCase("- wait-until Foo.Bar true 5 extra", 4)]
        public void WaitUntil_WithWrongArgumentCount_IsAnError(string line, int got)
        {
            var file = Parse(line);
            Assert.AreEqual(1, file.Errors.Count);
            StringAssert.Contains($"'wait-until' takes 2-3 arguments, got {got}", file.Errors[0]);
        }

        static string Owner => typeof(PlaytestWaitUntilTests).FullName!;

        [Test]
        public void ReadMember_ReadsFieldPropertyAndMethod()
        {
            Assert.AreEqual("True", PlaytestRunner.MemberText(PlaytestRunner.ReadMember(Owner + ".Flag", "wait-until")));
            Assert.AreEqual("3", PlaytestRunner.MemberText(PlaytestRunner.ReadMember(Owner + ".Count", "wait-until")));
            Assert.AreEqual("ready", PlaytestRunner.MemberText(PlaytestRunner.ReadMember(Owner + ".Name", "wait-until")));
            Assert.AreEqual("null", PlaytestRunner.MemberText(null));
        }

        [Test]
        public void ReadMember_MissingMember_IsArgumentException_ThrowingMember_IsInvocationException()
        {
            Assert.Throws<ArgumentException>(() => PlaytestRunner.ReadMember(Owner + ".Missing", "wait-until"));
            Assert.Throws<ArgumentException>(() => PlaytestRunner.ReadMember("NoSuchType_Mx.Value", "wait-until"));
            Assert.Throws<TargetInvocationException>(() => PlaytestRunner.ReadMember(Owner + ".Throws", "wait-until"));
        }
    }
}
