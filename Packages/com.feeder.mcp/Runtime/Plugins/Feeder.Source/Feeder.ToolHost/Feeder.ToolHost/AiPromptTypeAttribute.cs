using System;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Class)]
public class AiPromptTypeAttribute : Attribute
{
	public string? Path { get; set; }
}
}
