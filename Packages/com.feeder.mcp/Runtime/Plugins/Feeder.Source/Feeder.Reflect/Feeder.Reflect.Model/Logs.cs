using System.Collections.Generic;
using System.Text;

namespace Feeder.Reflect.Model
{
public class Logs : LinkedList<LogEntry>
{
	private const int DepthPadding = 2;

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		using (LinkedList<LogEntry>.Enumerator enumerator = GetEnumerator())
		{
			while (enumerator.MoveNext())
			{
				LogEntry current = enumerator.Current;
				stringBuilder.AppendLine(current.ToString());
			}
		}
		return stringBuilder.ToString();
	}
}
}
