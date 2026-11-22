using System.Threading;
using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost.Common.Hub.Client
{
public interface IClientSystemToolHub
{
	Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default(CancellationToken));

	Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default(CancellationToken));
}
}
