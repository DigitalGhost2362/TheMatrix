namespace Feeder.Json.Serialization
{
    // only the cycle-ignoring handler exists; any handler set on the options means "write null for a back reference"
    public sealed class ReferenceHandler
    {
        ReferenceHandler()
        {
        }

        public static ReferenceHandler IgnoreCycles { get; } = new ReferenceHandler();
    }
}
