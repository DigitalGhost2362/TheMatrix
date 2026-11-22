using System;
using System.Collections.Generic;
using Feeder.ToolHost.Common.Hub.Client;
using Feeder.Rx;

namespace Feeder.ToolHost
{
public interface IResourceManager : IClientResourceHub, IDisposable
{
	Observable<Unit> OnResourcesUpdated { get; }

	int EnabledResourcesCount { get; }

	int TotalResourcesCount { get; }

	IEnumerable<IRunResource> GetAllResources();

	bool HasResource(string name);

	bool AddResource(IRunResource resourceParams);

	bool RemoveResource(string name);

	bool IsResourceEnabled(string name);

	bool SetResourceEnabled(string name, bool enabled);
}
}
