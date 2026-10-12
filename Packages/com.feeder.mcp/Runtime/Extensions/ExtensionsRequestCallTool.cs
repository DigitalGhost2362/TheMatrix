#nullable enable
using System.Collections.Generic;
using Feeder.Json;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect;

namespace Feeder.MCP.Runtime.Extensions
{
    public static class ExtensionsRequestCallTool
    {
        public static RequestCallTool SetName(this RequestCallTool data, string name)
        {
            data.Name = name;
            return data;
        }
        public static RequestCallTool SetOrAddParameter(this RequestCallTool data, string name, object? value)
        {
            data.Arguments ??= value == null
                ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>() { [name] = value.ToJsonElement(MatrixPluginRuntime.Instance.ToolHostInstance?.McpManager.Reflector) };
            return data;
        }
        // public static IRequestData BuildRequest(this IRequestTool data)
        //     => new RequestData(data as RequestTool ?? throw new System.InvalidOperationException("CommandData is null"));
    }
}
