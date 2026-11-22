#nullable enable

using System;
using Feeder.MCP.Editor.Utils;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.Tools
{
    public sealed class UpdateCheckTests
    {
        [TestCase("com.feeder.mcp@https://github.com/org/TheMatrix.git?path=/Packages/com.feeder.mcp#main", "https://github.com/org/TheMatrix.git")]
        [TestCase("com.feeder.mcp@https://github.com/org/TheMatrix.git", "https://github.com/org/TheMatrix.git")]
        [TestCase("com.feeder.mcp@git+https://host/x.git#1.2.3", "https://host/x.git")]
        [TestCase("com.feeder.mcp@git@github.com:org/x.git?path=/p", "git@github.com:org/x.git")]
        public void ParseGitUrl_StripsPathAndRevision(string packageId, string expected)
            => Assert.AreEqual(expected, UpdateCheck.ParseGitUrl(packageId));

        [TestCase("com.feeder.mcp@file:../local")]
        [TestCase("")]
        [TestCase(null)]
        public void ParseGitUrl_NonGit_IsNull(string? packageId)
            => Assert.IsNull(UpdateCheck.ParseGitUrl(packageId));

        [Test]
        public void LatestTagVersion_PicksHighestStableTag()
        {
            const string output =
                "aaa\trefs/tags/v1.0.0\n" +
                "bbb\trefs/tags/1.10.0\n" +
                "ccc\trefs/tags/v1.9.5\n" +
                "ddd\trefs/tags/v2.0.0-beta.1\n" +
                "eee\trefs/tags/com.feeder.mcp/1.2.3\n" +
                "fff\trefs/tags/release-notes\n";
            Assert.AreEqual("1.10.0", UpdateCheck.LatestTagVersion(output));
        }

        [Test]
        public void LatestTagVersion_NoTags_IsNull()
        {
            Assert.IsNull(UpdateCheck.LatestTagVersion(string.Empty));
            Assert.IsNull(UpdateCheck.LatestTagVersion("aaa\trefs/tags/nightly\n"));
        }

        [TestCase("1.0.2", "1.0.1", true)]
        [TestCase("1.0.1", "1.0.1", false)]
        [TestCase("1.0.0", "1.0.1", false)]
        [TestCase("1.10.0", "1.9.9", true)]
        [TestCase("1.0.2", "1.0.1-preview.2", true)]
        [TestCase("garbage", "1.0.1", false)]
        [TestCase(null, "1.0.1", false)]
        public void IsNewer_ComparesNumerically(string? latest, string installed, bool expected)
            => Assert.AreEqual(expected, UpdateCheck.IsNewer(latest, installed));

        [Test]
        public void IsDue_OncePerUtcDay()
        {
            var now = new DateTime(2026, 11, 5, 23, 59, 0, DateTimeKind.Utc);
            Assert.IsTrue(UpdateCheck.IsDue(null, now));
            Assert.IsTrue(UpdateCheck.IsDue("2026-11-04", now));
            Assert.IsFalse(UpdateCheck.IsDue("2026-11-05", now));
        }
    }
}
