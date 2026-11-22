using Feeder.Reflect;
using Feeder.Logging;

namespace Feeder.ToolHost
{
internal static class RunToolFactory
{
	public static IRunTool Create(ToolMethodData method, Reflector reflector, ILogger? logger)
	{
		AiToolAttribute attribute = method.Attribute;
		if (!method.MethodInfo.IsStatic)
		{
			return RunTool.CreateFromClassMethod(reflector, logger, attribute.Name, method.ClassType, method.MethodInfo, attribute.Title, attribute.ReadOnlyHintValue, attribute.DestructiveHintValue, attribute.IdempotentHintValue, attribute.OpenWorldHintValue, attribute.EnabledValue, attribute.ToolType);
		}
		return RunTool.CreateFromStaticMethod(reflector, logger, attribute.Name, method.MethodInfo, attribute.Title, attribute.ReadOnlyHintValue, attribute.DestructiveHintValue, attribute.IdempotentHintValue, attribute.OpenWorldHintValue, attribute.EnabledValue, attribute.ToolType);
	}
}
}
