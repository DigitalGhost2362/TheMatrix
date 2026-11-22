using System.Threading.Tasks;
using Feeder.ToolHost.Common.Model;

namespace Feeder.ToolHost.Common.Hub.Client
{
public interface IClientResourceHub
{
	Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent request);

	Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources request);

	Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates request);
}
}
