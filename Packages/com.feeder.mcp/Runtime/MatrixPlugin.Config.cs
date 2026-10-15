#nullable enable
using System;
using System.Collections.Generic;
using Feeder.Json.Serialization;
using Feeder.ToolHost;
using Feeder.ToolHost.Common;
using Feeder.ToolHost.Common.Utils;
using Feeder.MCP.Runtime.Utils;
using static Feeder.ToolHost.Common.Consts.MCP.Server;

namespace Feeder.MCP
{
    public partial class MatrixPlugin
    {
        protected readonly object configMutex = new();

        protected UnityConnectionConfig unityConnectionConfig = null!; // Set by subclass constructors

        public class UnityConnectionConfig : ConnectionConfig
        {
            public static string DefaultHost => $"http://localhost:{GeneratePortFromDirectory()}";

            public static List<McpFeature> DefaultTools => new();

            /// <summary>
            /// Backing field for the local server URL. Serialized as "host" in JSON.
            /// <see cref="Host"/> is the same value under the name the connection layer reads.
            /// </summary>
            [JsonPropertyName("host")]
            public string LocalHost { get; set; } = DefaultHost;

            /// <summary>
            /// Backing field for the local auth token. Serialized as "token" in JSON.
            /// <see cref="Token"/> is the same value under the name the connection layer reads.
            /// </summary>
            [JsonPropertyName("token")]
            public string? LocalToken { get; set; }

            [JsonIgnore]
            public override string Host
            {
                get => LocalHost;
                set => LocalHost = value;
            }

            [JsonIgnore]
            public override string? Token
            {
                get => LocalToken;
                set => LocalToken = value;
            }

            public LogLevel LogLevel { get; set; } = LogLevel.Warning;
            public bool KeepServerRunning { get; set; } = false;
            public TransportMethod TransportMethod { get; set; } = TransportMethod.streamableHttp;
            public AuthOption AuthOption { get; set; } = AuthOption.none;
            public List<McpFeature> Tools { get; set; } = new();
            public Dictionary<string, bool> SkillAutoGenerate { get; set; } = new() { ["claude-code"] = true, ["deepseek"] = true };

            /// <summary>How long a tool call waits for the editor to reconnect after a domain reload. Read when the server starts.</summary>
            public int ReconnectGraceMs { get; set; } = DefaultReconnectGraceMs;

            /// <summary>Deadline of a tool that answers later, after a compile or domain reload. Read when the server starts.</summary>
            public int DeferredToolTimeoutMs { get; set; } = DefaultDeferredToolTimeoutMs;

            /// <summary>Lists the server's <c>tool-call</c> proxy, which calls a just-enabled tool before the client re-lists. Read when the server starts.</summary>
            public bool EnableToolCallProxy { get; set; } = true;

            public const int DefaultReconnectGraceMs = 180_000;
            public const int DefaultDeferredToolTimeoutMs = 900_000;

            /// <summary>
            /// Per-agent opt-in for writing the agent's MCP config file automatically on editor
            /// load (e.g. Claude Code's project-root <c>.mcp.json</c>). Keyed by agentId.
            /// Defaults to enabled for Claude Code and DeepSeek, and only takes effect once Matrix
            /// has been turned on for the project.
            /// </summary>
            public Dictionary<string, bool> AgentAutoConfigure { get; set; } = new() { ["claude-code"] = true, ["deepseek"] = true };

            /// <summary>
            /// When non-null, only the tools whose names appear in this list are enabled;
            /// all others are disabled. Set by the <c>FEEDER_MATRIX_TOOLS</c> environment variable
            /// (comma-separated tool IDs). Not persisted to disk.
            /// </summary>
            [JsonIgnore]
            public List<string>? EnabledToolsOverride { get; set; }

            public UnityConnectionConfig()
            {
                SetDefault();
            }

            public UnityConnectionConfig SetDefault()
            {
                Host = DefaultHost;
                var isCi = EnvironmentUtils.IsCi();
                KeepConnected = !isCi;
                KeepServerRunning = !isCi;
                GenerateSkillFiles = false;
                SkillsPath = ".claude/skills"; // default skills location for Claude Code
                SkillAutoGenerate = new() { ["claude-code"] = true, ["deepseek"] = true };
                AgentAutoConfigure = new() { ["claude-code"] = true, ["deepseek"] = true };
                TransportMethod = TransportMethod.streamableHttp;
                AuthOption = AuthOption.none;
                LogLevel = LogLevel.Warning;
                TimeoutMs = Consts.Hub.DefaultTimeoutMs;
                ReconnectGraceMs = DefaultReconnectGraceMs;
                DeferredToolTimeoutMs = DefaultDeferredToolTimeoutMs;
                EnableToolCallProxy = true;
                Tools = DefaultTools;
                Token = GenerateToken();
                return this;
            }

            public class McpFeature
            {
                public string Name { get; set; } = string.Empty;
                public bool Enabled { get; set; } = true;

                public McpFeature() { }
                public McpFeature(string name, bool enabled)
                {
                    Name = name;
                    Enabled = enabled;
                }
            }
        }
    }
}
