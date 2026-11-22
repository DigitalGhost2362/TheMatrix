namespace Feeder.ToolHost.Common.Model
{
public class RequestToolCompletedData
{
	public string RequestId { get; set; } = string.Empty;

	public ResponseCallTool Result { get; set; }

	public override string ToString()
	{
		return $"RequestId: {RequestId}, Result: {Result}";
	}
}
}
