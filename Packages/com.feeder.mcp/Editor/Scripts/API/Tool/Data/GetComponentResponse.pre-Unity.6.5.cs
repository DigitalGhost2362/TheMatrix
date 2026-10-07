#nullable enable
#if !UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.Json.Serialization;
using Feeder.ToolHost;
using Feeder.Reflect.Model;
using Feeder.MCP.Data;

namespace Feeder.MCP.Data
{
    public class GetComponentResponse
    {
        [Description("Reference to the component for future operations.")]
        public ComponentRef? Reference { get; set; }

        [Description("Index of the component in the GameObject's component list.")]
        public int Index { get; set; }

        [Description("Basic component information (type, enabled state).")]
        public ComponentDataShallow? Component { get; set; }

        [Description("Serialized fields of the component. Populated only on the legacy code path " +
            "(no 'paths' / no 'viewQuery').")]
        public List<SerializedMember>? Fields { get; set; }

        [Description("Serialized properties of the component. Populated only on the legacy code path " +
            "(no 'paths' / no 'viewQuery').")]
        public List<SerializedMember>? Properties { get; set; }

        [Description("Path-scoped read or view-query result, populated when 'paths' or 'viewQuery' was supplied. " +
            "Null otherwise.")]
        public SerializedMember? View { get; set; }
    }
}
#endif
