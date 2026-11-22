using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost.Common.Hub.Client
{
public interface IClientToolHub
{
	Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request);

	Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default(CancellationToken));
}
}
