using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Feeder.Reflect.Model
{
[Serializable]
public class SerializedMemberList : List<SerializedMember>
{
	public SerializedMemberList()
	{
	}

	public SerializedMemberList(int capacity)
		: base(capacity)
	{
	}

	public SerializedMemberList(SerializedMember item)
		: base(1)
	{
		Add(item);
	}

	public SerializedMemberList(IEnumerable<SerializedMember> collection)
		: base(collection)
	{
	}

	public SerializedMember? GetField(string name)
	{
		return this.FirstOrDefault((SerializedMember x) => x.name == name);
	}

	public override string ToString()
	{
		if (base.Count == 0)
		{
			return "No items";
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine($"Items total amount: {base.Count}");
		for (int i = 0; i < base.Count; i++)
		{
			stringBuilder.AppendLine($"Item[{i}] {base[i]}");
		}
		return stringBuilder.ToString();
	}
}
}
