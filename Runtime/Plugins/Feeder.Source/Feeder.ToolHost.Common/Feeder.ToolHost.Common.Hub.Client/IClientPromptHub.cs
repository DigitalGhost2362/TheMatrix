using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost.Common.Hub.Client
{
public interface IClientPromptHub
{
	Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request);

	Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request);
}
}
