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
    public class ShaderMessageData
    {
        [Description("The error or warning message text.")]
        public string? Message { get; set; }

        [Description("The line number in the shader source where the issue occurs.")]
        public int Line { get; set; }

        [Description("Severity level (e.g. 'Error', 'Warning').")]
        public string? Severity { get; set; }

        [Description("The platform on which the error occurs (e.g. 'OpenGLCore', 'D3D11').")]
        public string? Platform { get; set; }
    }
}
