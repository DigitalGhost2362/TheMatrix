#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.Json.Serialization;
using Feeder.ToolHost;
using Feeder.Reflect.Model;
using Feeder.MCP.Data;

namespace Feeder.MCP.Data
{
    public class ToolToggleResult
    {
        [Description("Optional operation logs. Only included when 'includeLogs' is true.")]
        public Logs? Logs { get; set; }

        [Description("Result of each tool operation. Key: original input name as provided by the caller (case preserved as-is). Value: true if the enable/disable operation completed successfully, false if the name was unknown, ambiguous, or empty.")]
        public Dictionary<string, bool> Success { get; set; } = new();
    }
}
