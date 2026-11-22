using System.Collections.Generic;
using Feeder.ToolHost.Common.Hub.Client;

namespace Feeder.ToolHost
{
public interface ISystemToolManager : IClientSystemToolHub
{
	int TotalToolsCount { get; }

	IEnumerable<IRunTool> GetAllTools();

	bool HasTool(string name);
}
}
