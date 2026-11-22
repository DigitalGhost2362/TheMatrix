using System;
using System.Collections.Generic;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect;
using Feeder.Rx;

namespace Feeder.ToolHost
{
public interface IMcpManager : IDisposable
{
	Reflector Reflector { get; }

	IReadOnlyList<McpClientData> ActiveClients { get; }

	Observable<McpClientData> OnClientConnected { get; }

	Observable<McpClientData> OnClientDisconnected { get; }

	Observable<IReadOnlyList<McpClientData>> OnClientsChanged { get; }

	IToolManager? ToolManager { get; }

	IPromptManager? PromptManager { get; }

	IResourceManager? ResourceManager { get; }

	ISystemToolManager? SystemToolManager { get; }
}
}
