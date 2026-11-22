using System;

namespace Feeder.ToolHost
{
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class RequestIDAttribute : Attribute
{
}
}
