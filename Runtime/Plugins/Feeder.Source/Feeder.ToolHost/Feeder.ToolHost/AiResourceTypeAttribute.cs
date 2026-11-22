using System;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Class)]
public class AiResourceTypeAttribute : Attribute
{
	public string? Path { get; set; }
}
}
