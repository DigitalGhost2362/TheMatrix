using System.Reflection;
using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
public interface IReflectorModuleContext
{
	Reflector Reflector { get; }

	IScanIgnoreBuilder Scan { get; }

	Assembly OwningAssembly { get; }

	ILogger Logger { get; }
}
}
