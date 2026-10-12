#nullable enable
#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.Json.Serialization;
using Feeder.ToolHost;
using Feeder.Reflect.Model;
using Feeder.MCP.Data;

namespace Feeder.MCP.Data
{
    public class DestroyGameObjectResult
    {
        [Description("Name of the destroyed GameObject.")]
        public string? DestroyedName { get; set; }

        [Description("Hierarchy path of the destroyed GameObject.")]
        public string? DestroyedPath { get; set; }

        [Description("Instance ID of the destroyed GameObject.")]
        public UnityEngine.EntityId DestroyedInstanceId { get; set; }
    }
}
#endif
