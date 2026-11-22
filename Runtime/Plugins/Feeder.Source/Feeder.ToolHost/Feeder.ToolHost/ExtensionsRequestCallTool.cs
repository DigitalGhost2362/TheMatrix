using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost
{
public static class ExtensionsRequestCallTool
{
	public static RequestCallTool SetName(this RequestCallTool data, string name)
	{
		data.Name = name;
		return data;
	}
}
}
