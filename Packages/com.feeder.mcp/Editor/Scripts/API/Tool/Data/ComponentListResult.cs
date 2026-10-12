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
    public class ComponentListResult
    {
        [Description("Array of component type names for the current page.")]
        public string[] Items { get; set; } = Array.Empty<string>();

        [Description("Current page number (0-based).")]
        public int Page { get; set; }

        [Description("Number of items per page.")]
        public int PageSize { get; set; }

        [Description("Total number of matching components.")]
        public int TotalCount { get; set; }

        [Description("Total number of pages available.")]
        public int TotalPages { get; set; }
    }
}
