#nullable enable
using System;

namespace Feeder.MCP.Editor.API.TestRunner
{
    public class TestResultData
    {
        public string Name { get; set; } = string.Empty;
        public TestResultStatus Status { get; set; } = TestResultStatus.Skipped;
        public TimeSpan Duration { get; set; }
        public string? Message { get; set; }
        public string? StackTrace { get; set; }
    }
}
