#nullable enable

using System.Collections.Generic;
using Feeder.MCP.Runtime.Utils;
using NUnit.Framework;
using AuthOption = Feeder.ToolHost.Common.Consts.MCP.Server.AuthOption;
using TransportMethod = Feeder.ToolHost.Common.Consts.MCP.Server.TransportMethod;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class EnvironmentUtilsTests
    {
        const string DiskHost = "http://localhost:21000";

        static MatrixPlugin.UnityConnectionConfig DiskConfig() => new MatrixPlugin.UnityConnectionConfig
        {
            LocalHost = DiskHost,
            LocalToken = "disk-token",
            KeepConnected = true,
            KeepServerRunning = false,
            AuthOption = AuthOption.none,
            TransportMethod = TransportMethod.streamableHttp,
            EnabledToolsOverride = null
        };

        static EnvironmentUtils.OverrideRecord Apply(
            MatrixPlugin.UnityConnectionConfig config,
            Dictionary<string, string>? args = null,
            Dictionary<string, string>? env = null)
        {
            var environment = env ?? new Dictionary<string, string>();
            return EnvironmentUtils.ApplyEnvironmentOverrides(
                config,
                args ?? new Dictionary<string, string>(),
                key => environment.TryGetValue(key, out var value) ? value : null);
        }

        [Test]
        public void NoSources_ChangeNothing()
        {
            var config = DiskConfig();

            var record = Apply(config);

            Assert.IsFalse(record.HasAny);
            Assert.AreEqual(DiskHost, config.LocalHost);
            Assert.AreEqual("disk-token", config.LocalToken);
        }

        [Test]
        public void LoopbackHost_OverridesAndRemembersTheDiskValue()
        {
            var config = DiskConfig();

            var record = Apply(config, env: new() { [EnvironmentUtils.EnvHost] = "http://127.0.0.1:25000/" });

            Assert.AreEqual("http://127.0.0.1:25000", config.LocalHost);
            Assert.IsTrue(record.Contains(EnvironmentUtils.FieldHost));
            Assert.AreEqual(DiskHost, record.BaselineValues[EnvironmentUtils.FieldHost]);
        }

        [Test]
        public void RemoteHost_IsIgnored()
        {
            var config = DiskConfig();

            var record = Apply(config, env: new() { [EnvironmentUtils.EnvHost] = "http://example.com:25000" });

            Assert.AreEqual(DiskHost, config.LocalHost);
            Assert.IsFalse(record.HasAny);
        }

        [Test]
        public void CommandLineFlag_BeatsEnvironmentVariable()
        {
            var config = DiskConfig();

            Apply(config,
                args: new() { [EnvironmentUtils.EnvKeepConnected] = "false" },
                env: new() { [EnvironmentUtils.EnvKeepConnected] = "true" });

            Assert.IsFalse(config.KeepConnected);
        }

        [Test]
        public void ValueEqualToDisk_IsNotTracked()
        {
            var config = DiskConfig();

            var record = Apply(config, env: new() { [EnvironmentUtils.EnvKeepConnected] = "true" });

            Assert.IsFalse(record.HasAny);
        }

        [Test]
        public void QuotedAndPaddedValues_AreSanitized()
        {
            var config = DiskConfig();

            Apply(config, env: new()
            {
                [EnvironmentUtils.EnvAuthOption] = "  \"Required\" ",
                [EnvironmentUtils.EnvTransport] = "\"STDIO\"",
                [EnvironmentUtils.EnvStartServer] = " true "
            });

            Assert.AreEqual(AuthOption.required, config.AuthOption);
            Assert.AreEqual(TransportMethod.stdio, config.TransportMethod);
            Assert.IsTrue(config.KeepServerRunning);
        }

        [Test]
        public void UnparsableValues_AreIgnored()
        {
            var config = DiskConfig();

            var record = Apply(config, env: new()
            {
                [EnvironmentUtils.EnvKeepConnected] = "maybe",
                [EnvironmentUtils.EnvAuthOption] = "sometimes",
                [EnvironmentUtils.EnvHost] = "not a url"
            });

            Assert.IsFalse(record.HasAny);
            Assert.IsTrue(config.KeepConnected);
            Assert.AreEqual(AuthOption.none, config.AuthOption);
        }

        [Test]
        public void ToolList_IsTrimmedAndDeduplicatedIgnoringCase()
        {
            var config = DiskConfig();

            Apply(config, env: new() { [EnvironmentUtils.EnvTools] = " ping , gameobject-find,PING ,, \"console-get-logs\" " });

            CollectionAssert.AreEqual(new[] { "ping", "gameobject-find", "console-get-logs" }, config.EnabledToolsOverride);
        }

        [Test]
        public void Baseline_RestoresDiskValues_ThenOverridesReapply()
        {
            var config = DiskConfig();
            var record = Apply(config, env: new()
            {
                [EnvironmentUtils.EnvToken] = "runtime-token",
                [EnvironmentUtils.EnvKeepConnected] = "false"
            });

            EnvironmentUtils.ApplyBaseline(config, record);
            Assert.AreEqual("disk-token", config.LocalToken);
            Assert.IsTrue(config.KeepConnected);

            EnvironmentUtils.ApplyOverrides(config, record);
            Assert.AreEqual("runtime-token", config.LocalToken);
            Assert.IsFalse(config.KeepConnected);
        }

        [TestCase("http://localhost:8080", true)]
        [TestCase("http://LOCALHOST", true)]
        [TestCase("http://127.0.0.1:1", true)]
        [TestCase("http://127.10.20.30", true)]
        [TestCase("http://[::1]:5000", true)]
        [TestCase("http://192.168.0.10:8080", false)]
        [TestCase("http://localhost.example.com", false)]
        [TestCase("localhost:8080", false)]
        [TestCase("", false)]
        public void IsLoopbackUrl(string url, bool expected)
        {
            Assert.AreEqual(expected, EnvironmentUtils.IsLoopbackUrl(url));
        }
    }
}
