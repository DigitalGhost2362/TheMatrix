using System.Runtime.InteropServices;

namespace Feeder.ToolHost.AgentConfig
{
public static class HostOperatingSystem
{
	public static OperatingSystemKind Detect()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return OperatingSystemKind.Windows;
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return OperatingSystemKind.MacOS;
		}
		return OperatingSystemKind.Linux;
	}
}
}
