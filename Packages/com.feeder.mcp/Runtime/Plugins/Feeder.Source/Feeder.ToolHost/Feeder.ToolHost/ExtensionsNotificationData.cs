using System.Collections.Generic;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost
{
public static class ExtensionsNotificationData
{
	public static IRequestNotification SetName(this IRequestNotification data, string name)
	{
		data.Name = name;
		return data;
	}

	public static IRequestNotification SetOrAddParameter(this IRequestNotification data, string name, object? value)
	{
		if (data.Parameters == null)
		{
			IDictionary<string, object> dictionary = (data.Parameters = new Dictionary<string, object>());
		}
		data.Parameters[name] = value;
		return data;
	}
}
}
