#nullable enable
#if !UNITY_6000_5_OR_NEWER

using Feeder.Json;
using Feeder.MCP.Data;
using Feeder.MCP.JsonConverters;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests.RefTypes
{
    public sealed class InstanceIdReadingTests
    {
        static JsonSerializerOptions Options()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new ObjectRefConverter());
            options.Converters.Add(new GameObjectRefConverter());
            return options;
        }

        [TestCase("{\"instanceID\":-4996}", -4996)]
        [TestCase("{\"instanceID\":\"-4996\"}", -4996)]
        [TestCase("{\"instanceID\":\"512\"}", 512)]
        [TestCase("{\"instanceId\":\"512\"}", 512)]
        [TestCase("{\"instanceID\":null}", 0)]
        public void ObjectRef_ReadsNumberStringAndNullIds(string json, int expected)
        {
            Assert.AreEqual(expected, JsonSerializer.Deserialize<ObjectRef>(json, Options())!.InstanceID);
            Assert.AreEqual(expected, JsonSerializer.Deserialize<GameObjectRef>(json, Options())!.InstanceID);
        }

        [TestCase("{\"instanceID\":\"\"}")]
        [TestCase("{\"instanceID\":\"abc\"}")]
        [TestCase("{\"instanceID\":\"5.6810558E+17\"}")]
        public void ObjectRef_RejectsNonIntegerStrings(string json)
        {
            Assert.Catch<JsonException>(() => JsonSerializer.Deserialize<ObjectRef>(json, Options()));
        }
    }
}
#endif
