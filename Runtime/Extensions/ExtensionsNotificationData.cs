#nullable enable
using System.Collections.Generic;
using Feeder.ToolHost.Common.Model;

namespace Feeder.MCP.Runtime.Extensions
{
    public static class ExtensionsNotificationData
    {
        public static RequestNotification SetName(this RequestNotification data, string name)
        {
            data.Name = name;
            return data;
        }
        public static RequestNotification SetOrAddParameter(this RequestNotification data, string name, object? value)
        {
            data.Parameters ??= new Dictionary<string, object?>();
            data.Parameters[name] = value;
            return data;
        }
        // public static IRequestCallTool Build(this IRequestNotification data)
        //     => new RequestData(data as RequestNotification ?? throw new System.InvalidOperationException("NotificationData is null"));
    }
}
