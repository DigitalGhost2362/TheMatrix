namespace Feeder.Json
{
    public partial class JsonException
    {
        // set on the serializer's own failures: the path and position are appended to their message
        internal bool AppendPathInformation { get; set; }
    }
}
