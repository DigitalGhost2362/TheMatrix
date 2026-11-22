#nullable enable

using Feeder.MCP.Editor.HyperTesting;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class CloneEnvironmentTests
    {
        [TestCase("FEEDER_MATRIX_PORT")]
        [TestCase("feeder_matrix_token")]
        [TestCase("MCP_PLUGIN_PORT")]
        [TestCase("OTHER_MCP_HOST")]
        [TestCase("SOME_TOOL_MCP_TOKEN")]
        public void IsMcpEnvironmentKey_McpVariable_True(string key)
        {
            Assert.IsTrue(HyperClonePool.IsMcpEnvironmentKey(key));
        }

        [TestCase("PATH")]
        [TestCase("USERPROFILE")]
        [TestCase("UNITY_LICENSING_SERVER")]
        [TestCase("DUMPCP_HOME")]
        public void IsMcpEnvironmentKey_OtherVariable_False(string key)
        {
            Assert.IsFalse(HyperClonePool.IsMcpEnvironmentKey(key));
        }
    }
}
