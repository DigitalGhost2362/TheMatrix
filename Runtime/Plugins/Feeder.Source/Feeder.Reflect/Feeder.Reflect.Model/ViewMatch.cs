using System.ComponentModel;

namespace Feeder.Reflect.Model
{
public class ViewMatch
{
	[Description("Full slash-delimited path to the matched location within the object graph. Array elements use bracket notation. Examples: 'orbitRadius', 'celestialBodies/[0]/orbitRadius', 'config/[timeout]'.")]
	public string Path { get; }

	[Description("Serialized representation of the value found at the matched path.")]
	public SerializedMember Value { get; }

	public ViewMatch(string path, SerializedMember value)
	{
		Path = path;
		Value = value;
	}
}
}
