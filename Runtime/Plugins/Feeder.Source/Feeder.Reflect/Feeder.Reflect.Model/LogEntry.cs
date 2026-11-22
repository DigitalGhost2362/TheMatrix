using Feeder.Reflect.Utils;

namespace Feeder.Reflect.Model
{
public class LogEntry
{
	public int Depth { get; set; }

	public string Message { get; set; } = string.Empty;

	public LogType Type { get; set; } = LogType.Info;

	public override string ToString()
	{
		string padding = StringUtils.GetPadding(Depth);
		return string.Format("{0}[{1}] {2}", padding, Type, Message.Replace("\n", "\n" + padding));
	}
}
}
