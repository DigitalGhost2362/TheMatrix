#nullable enable

using System;
using System.IO;
using Feeder.MCP.Editor;
using Feeder.MCP.Runtime.Utils;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Utils
{
    public sealed class ServerPathSettingTests
    {
        string? savedEnv;
        string? tempFile;

        [SetUp]
        public void SetUp()
        {
            savedEnv = Environment.GetEnvironmentVariable(EnvironmentUtils.EnvServerPath);
            Environment.SetEnvironmentVariable(EnvironmentUtils.EnvServerPath, null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(EnvironmentUtils.EnvServerPath, savedEnv);
            if (tempFile != null && File.Exists(tempFile))
                File.Delete(tempFile);
        }

        [Test]
        public void DotEnv_ParsesPairsQuotesAndComments()
        {
            var values = EnvironmentUtils.ParseDotEnv(
                "# comment\r\n\r\nA=1\nB = \"two words\"\nC='single'\nexport D=four # trailing\nE=C:\\tools\\srv.exe\nnot a pair\n=novalue\n");

            Assert.AreEqual("1", values["A"]);
            Assert.AreEqual("two words", values["B"]);
            Assert.AreEqual("single", values["C"]);
            Assert.AreEqual("four", values["D"]);
            Assert.AreEqual("C:\\tools\\srv.exe", values["E"]);
            Assert.AreEqual(5, values.Count);
        }

        [Test]
        public void DotEnv_NullOrEmpty_IsEmpty()
        {
            Assert.IsEmpty(EnvironmentUtils.ParseDotEnv(null));
            Assert.IsEmpty(EnvironmentUtils.ParseDotEnv(string.Empty));
        }

        [Test]
        public void Setting_ProcessEnvironmentWinsOverDotEnv()
        {
            var dotEnv = $"{EnvironmentUtils.EnvServerPath}=from-file\n";
            Assert.AreEqual("from-env", EnvironmentUtils.ReadServerPathSetting(_ => " \"from-env\" ", dotEnv));
            Assert.AreEqual("from-file", EnvironmentUtils.ReadServerPathSetting(_ => null, dotEnv));
            Assert.IsNull(EnvironmentUtils.ReadServerPathSetting(_ => null, "OTHER=1"));
            Assert.IsNull(EnvironmentUtils.ReadServerPathSetting(_ => "  ", null));
        }

        [Test]
        public void Resolve_RelativePathsUseTheProjectRoot()
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "matrix-root"));
            Assert.AreEqual(Path.Combine(root, "bin", "srv.exe"), EnvironmentUtils.ResolveServerPath("bin/srv.exe", root));
            var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "abs.exe"));
            Assert.AreEqual(absolute, EnvironmentUtils.ResolveServerPath(absolute, root));
            Assert.IsNull(EnvironmentUtils.ResolveServerPath("", root));
            Assert.IsNull(EnvironmentUtils.ResolveServerPath(null, root));
        }

        [Test]
        public void ExistingFile_ReplacesTheBundledServer()
        {
            tempFile = Path.Combine(Path.GetTempPath(), $"matrix-custom-{Guid.NewGuid():N}.exe");
            File.WriteAllText(tempFile, "x");
            var bundled = McpServerManager.ExecutableFullPath;
            Environment.SetEnvironmentVariable(EnvironmentUtils.EnvServerPath, tempFile);

            Assert.AreEqual(Path.GetFullPath(tempFile), McpServerManager.ExecutableFullPath);
            Assert.IsTrue(McpServerManager.IsBinaryReadyToStart());
            Assert.AreEqual(ServerOwner.Ours, ServerOwnership.Classify(tempFile, McpServerManager.ExecutableFolderRootPath, McpServerManager.CustomServerPath));
            Assert.AreNotEqual(ServerOwner.Ours, ServerOwnership.Classify(tempFile, McpServerManager.ExecutableFolderRootPath));

            Environment.SetEnvironmentVariable(EnvironmentUtils.EnvServerPath, null);
            Assert.AreEqual(bundled, McpServerManager.ExecutableFullPath);
        }

        [Test]
        public void MissingFile_FallsBackToTheBundledServer()
        {
            var bundled = McpServerManager.ExecutableFullPath;
            Environment.SetEnvironmentVariable(EnvironmentUtils.EnvServerPath, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"));

            Assert.IsNull(McpServerManager.CustomServerPath);
            Assert.AreEqual(bundled, McpServerManager.ExecutableFullPath);
        }
    }
}
