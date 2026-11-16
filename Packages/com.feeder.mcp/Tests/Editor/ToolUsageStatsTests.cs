#nullable enable

using System;
using Feeder.MCP.Editor.UI;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class ToolUsageStatsTests
    {
        static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

        static string Line(string tool, DateTimeOffset at, string status = "ok", string? via = null)
            => "{\"at\":\"" + at.ToString("o") + "\",\"tool\":\"" + tool + "\",\"via\":" + (via == null ? "null" : "\"" + via + "\"") +
               ",\"client\":\"c\",\"agent\":\"main\",\"ms\":3,\"status\":\"" + status + "\",\"argBytes\":10,\"resultBytes\":20}";

        [Test]
        public void Aggregate_CountsSevenAndThirtyDays_ErrorsAndToolCall()
        {
            var usage = ToolUsageStats.Aggregate(new[]
            {
                Line("rql-query", Now.AddHours(-1)),
                Line("rql-query", Now.AddDays(-10), "error"),
                Line("rql-query", Now.AddDays(-40)),
                Line("scene-create", Now.AddDays(-2), "ok", "tool-call"),
                "not json at all",
            }, Now);

            var rql = usage["rql-query"];
            Assert.AreEqual(1, rql.Calls7);
            Assert.AreEqual(2, rql.Calls30);
            Assert.AreEqual(1, rql.Errors30);
            Assert.AreEqual(Now.AddHours(-1), rql.LastUsed);
            Assert.AreEqual(1, usage["scene-create"].ViaToolCall30);
            Assert.AreEqual(2, usage.Count);
        }

        [Test]
        public void Describe_SaysWhenAToolWasNotUsed()
        {
            Assert.AreEqual("not used in 30 days", ToolUsageStats.Describe(null, Now));
            var usage = ToolUsageStats.Aggregate(new[] { Line("x", Now.AddHours(-2), "timeout") }, Now)["x"];
            StringAssert.Contains("last 2h ago", ToolUsageStats.Describe(usage, Now));
            StringAssert.Contains("100", ToolUsageStats.Describe(usage, Now));
        }
    }
}
