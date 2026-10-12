using System;
using System.Collections.Generic;
using Feeder.ToolHost.Common.Hub.Client;
using Feeder.Rx;

namespace Feeder.ToolHost
{
public interface IPromptManager : IClientPromptHub, IDisposable
{
	Observable<Unit> OnPromptsUpdated { get; }

	int EnabledPromptsCount { get; }

	int TotalPromptsCount { get; }

	IEnumerable<IRunPrompt> GetAllPrompts();

	bool HasPrompt(string name);

	bool AddPrompt(IRunPrompt runner);

	bool RemovePrompt(string name);

	bool IsPromptEnabled(string name);

	bool SetPromptEnabled(string name, bool enabled);
}
}
