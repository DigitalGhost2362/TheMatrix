using System;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Class)]
public class AiToolTypeAttribute : Attribute
{
	public string? Path { get; set; }
}
}
