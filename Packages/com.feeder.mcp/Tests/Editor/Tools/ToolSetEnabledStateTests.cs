#nullable enable

using System;
using System.Linq;
using System.Threading;
using Feeder.MCP.Data;
using Feeder.MCP.Editor.API;
using Feeder.ToolHost;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class ToolSetEnabledStateTests
    {
        const string Target = Tool_Script.ScriptReadToolId;

        // the host drops tool-change events inside its 100 ms throttle, so the restore waits it out
        const int ThrottleWindowMs = 150;

        readonly Tool_Tool _tool = new Tool_Tool();
        IToolManager? _tools;
        bool _initial;

        [SetUp]
        public void RememberState()
        {
            _tools = MatrixPluginEditor.Instance.Tools;
            Assume.That(_tools, Is.Not.Null, "Matrix Bridge is not active, so no tool manager is built.");
            Assume.That(_tools!.HasTool(Target), Is.True, $"'{Target}' is not registered.");
            _initial = _tools.IsToolEnabled(Target);
        }

        [TearDown]
        public void RestoreState()
        {
            if (_tools == null || !_tools.HasTool(Target) || _tools.IsToolEnabled(Target) == _initial)
                return;
            Thread.Sleep(ThrottleWindowMs);
            _tool.SetEnabledState(new[] { Toggle(Target, _initial) });
        }

        static ToolToggleInput Toggle(string name, bool enabled) => new ToolToggleInput { Name = name, Enabled = enabled };

        [Test]
        public void Flip_ChangesTheToolAndTheListShowsIt()
        {
            var result = _tool.SetEnabledState(new[] { Toggle(Target, !_initial) });

            Assert.IsTrue(result.Success[Target]);
            Assert.AreEqual(!_initial, _tools!.IsToolEnabled(Target));
            Assert.AreEqual(!_initial, _tool.List($"^{Target}$").Single().Enabled);
        }

        [Test]
        public void FlipAndBack_RestoresTheInitialState()
        {
            _tool.SetEnabledState(new[] { Toggle(Target, !_initial) });
            Thread.Sleep(ThrottleWindowMs);
            var back = _tool.SetEnabledState(new[] { Toggle(Target, _initial) });

            Assert.IsTrue(back.Success[Target]);
            Assert.AreEqual(_initial, _tools!.IsToolEnabled(Target));
        }

        [Test]
        public void AlreadyInState_SucceedsAndSaysSo()
        {
            var result = _tool.SetEnabledState(new[] { Toggle(Target, _initial) }, includeLogs: true);

            Assert.IsTrue(result.Success[Target]);
            Assert.AreEqual(_initial, _tools!.IsToolEnabled(Target));
            Assert.IsTrue(result.Logs!.Any(l => l.Message.Contains("already")));
        }

        [Test]
        public void NameIsResolvedIgnoringCase_KeyKeepsTheCallersSpelling()
        {
            var shouted = Target.ToUpperInvariant();

            var result = _tool.SetEnabledState(new[] { Toggle(shouted, !_initial) });

            Assert.IsTrue(result.Success[shouted]);
            Assert.AreEqual(!_initial, _tools!.IsToolEnabled(Target));
        }

        [Test]
        public void UnknownTool_FailsWithoutTouchingOthers()
        {
            var unknown = "mx-no-such-tool-" + Guid.NewGuid().ToString("N");

            var result = _tool.SetEnabledState(new[] { Toggle(unknown, true), Toggle(Target, _initial) }, includeLogs: true);

            Assert.IsFalse(result.Success[unknown]);
            Assert.IsTrue(result.Success[Target]);
            Assert.IsTrue(result.Logs!.Any(l => l.Message.Contains(unknown) && l.Message.Contains("not found")));
        }

        [Test]
        public void BlankName_Fails()
        {
            var result = _tool.SetEnabledState(new[] { Toggle(string.Empty, true) });

            Assert.IsFalse(result.Success[string.Empty]);
        }

        [Test]
        public void LogsAreOmittedUnlessAsked()
        {
            Assert.IsNull(_tool.SetEnabledState(new[] { Toggle(Target, _initial) }).Logs);
        }

        [Test]
        public void EmptyOrNullInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => _tool.SetEnabledState(Array.Empty<ToolToggleInput>()));
            Assert.Throws<ArgumentException>(() => _tool.SetEnabledState(null!));
        }
    }
}
