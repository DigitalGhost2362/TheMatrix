using System.Text.Json;
using System.Text.Json.Nodes;

namespace Feeder.McpPlugin.Common
{
public static class Consts
{
	public static class ContentType
	{
		public const string Text = "text";

		public const string Image = "image";

		public const string Audio = "audio";

		public const string Resource = "resource";
	}

	public static class Guid
	{
		public const string Zero = "00000000-0000-0000-0000-000000000000";
	}

	public static class Command
	{
		public static class ResponseCode
		{
			public const string Success = "[Success]";

			public const string Error = "[Error]";

			public const string Cancel = "[Cancel]";
		}
	}

	public static class MCP
	{
		public static class Plugin
		{
			public static class Args
			{
				public const string McpServerEndpoint = "mcp-server-endpoint";

				public const string McpServerTimeout = "mcp-server-timeout";

				public const string McpPluginToken = "mcp-plugin-token";

				public const string McpSkillsFolder = "mcp-skills-folder";
			}

			public static class Env
			{
				public const string McpServerEndpoint = "MCP_SERVER_ENDPOINT";

				public const string McpServerTimeout = "MCP_SERVER_TIMEOUT";

				public const string McpPluginToken = "MCP_PLUGIN_TOKEN";

				public const string McpSkillsFolder = "MCP_SKILLS_FOLDER";
			}

			public const int LinesLimit = 1000;
		}

		public static class Server
		{
			public static class Args
			{
				public const string Port = "port";

				public const string PluginTimeout = "plugin-timeout";

				public const string ClientTransportMethod = "client-transport";

				public const string Token = "token";

				public const string Authorization = "authorization";

				public const string IdleTimeoutSeconds = "idle-timeout-seconds";

				public const string MaxIdleSessionCount = "max-idle-session-count";

				public const string WebhookToolUrl = "webhook-tool-url";

				public const string WebhookPromptUrl = "webhook-prompt-url";

				public const string WebhookResourceUrl = "webhook-resource-url";

				public const string WebhookConnectionUrl = "webhook-connection-url";

				public const string WebhookToken = "webhook-token";

				public const string WebhookHeader = "webhook-header";

				public const string WebhookTimeout = "webhook-timeout";

				public const string WebhookAuthorizationUrl = "webhook-authorization-url";

				public const string WebhookAuthorizationFailOpen = "webhook-authorization-fail-open";
			}

			public static class Env
			{
				public const string Port = "MCP_PLUGIN_PORT";

				public const string PluginTimeout = "MCP_PLUGIN_CLIENT_TIMEOUT";

				public const string ClientTransportMethod = "MCP_PLUGIN_CLIENT_TRANSPORT";

				public const string Token = "MCP_PLUGIN_TOKEN";

				public const string Authorization = "MCP_AUTHORIZATION";

				public const string IdleTimeoutSeconds = "MCP_PLUGIN_IDLE_TIMEOUT_SECONDS";

				public const string MaxIdleSessionCount = "MCP_PLUGIN_MAX_IDLE_SESSION_COUNT";

				public const string WebhookToolUrl = "MCP_PLUGIN_WEBHOOK_TOOL_URL";

				public const string WebhookPromptUrl = "MCP_PLUGIN_WEBHOOK_PROMPT_URL";

				public const string WebhookResourceUrl = "MCP_PLUGIN_WEBHOOK_RESOURCE_URL";

				public const string WebhookConnectionUrl = "MCP_PLUGIN_WEBHOOK_CONNECTION_URL";

				public const string WebhookToken = "MCP_PLUGIN_WEBHOOK_TOKEN";

				public const string WebhookHeader = "MCP_PLUGIN_WEBHOOK_HEADER";

				public const string WebhookTimeout = "MCP_PLUGIN_WEBHOOK_TIMEOUT";

				public const string WebhookAuthorizationUrl = "MCP_PLUGIN_WEBHOOK_AUTHORIZATION_URL";

				public const string WebhookAuthorizationFailOpen = "MCP_PLUGIN_WEBHOOK_AUTHORIZATION_FAIL_OPEN";
			}

			public enum TransportMethod
			{
				unknown,
				stdio,
				streamableHttp
			}

			public enum AuthOption
			{
				unknown,
				none,
				required
			}

			public static class Headers
			{
				public const string TrustedInternalClient = "X-McpPlugin-Internal-Client";

				public const string TrustedInternalClientOptInValue = "1";
			}

			public const int DefaultIdleTimeoutSeconds = 600;

			public const int DefaultMaxIdleSessionCount = 1000;

			public const string DefaultBodyPath = "mcpServers";

			public const string DefaultServerName = "McpPlugin";

			public const string BodyPathDelimiter = "->";

			public static string[] BodyPathSegments(string bodyPath)
			{
				return bodyPath.Split("->");
			}

			public static JsonNode Config(string executablePath, string serverName = "McpPlugin", string bodyPath = "mcpServers", int port = 8080, int timeoutMs = 10000)
			{
				string[] array = BodyPathSegments(bodyPath);
				JsonObject jsonObject = new JsonObject();
				JsonObject jsonObject2 = jsonObject;
				string[] array2 = array;
				foreach (string propertyName in array2)
				{
					JsonObject jsonObject3 = (JsonObject)(jsonObject2[propertyName] = new JsonObject());
					jsonObject2 = jsonObject3;
				}
				jsonObject2[serverName] = new JsonObject
				{
					["type"] = "stdio",
					["command"] = executablePath,
					["args"] = new JsonArray
					{
						string.Format("{0}={1}", "port", port),
						string.Format("{0}={1}", "plugin-timeout", timeoutMs),
						string.Format("{0}={1}", "client-transport", TransportMethod.stdio)
					}
				};
				return jsonObject;
			}
		}

		public static readonly JsonElement EmptyInputSchema = JsonDocument.Parse("{ \"type\": \"object\", \"additionalProperties\": false }").RootElement;

		private static readonly JsonNode _emptyInputSchemaNodeTemplate = new JsonObject
		{
			["type"] = "object",
			["additionalProperties"] = false
		};

		public static JsonNode EmptyInputSchemaNode => _emptyInputSchemaNodeTemplate.DeepClone();
	}

	public static class MimeType
	{
		public const string TextPlain = "text/plain";

		public const string TextHtml = "text/html";

		public const string TextJson = "application/json";

		public const string TextXml = "application/xml";

		public const string TextYaml = "application/x-yaml";

		public const string TextCsv = "text/csv";

		public const string TextMarkdown = "text/markdown";

		public const string TextJavascript = "application/javascript";

		public const string ImagePng = "image/png";

		public const string ImageJpeg = "image/jpeg";

		public const string ImageGif = "image/gif";

		public const string ImageWebp = "image/webp";

		public const string ImageSvg = "image/svg+xml";

		public const string AudioMpeg = "audio/mpeg";

		public const string AudioWav = "audio/wav";

		public const string AudioOgg = "audio/ogg";

		public const string AudioWebm = "audio/webm";
	}

	public static class Log
	{
		public static class Color
		{
			public const string TagStart = "";

			public const string TagEnd = "";

			public const string LevelStart = "";

			public const string LevelEnd = "";

			public const string CategoryStart = "";

			public const string CategoryEnd = "";
		}

		public const string Tag = "[AI]";

		public const string Trce = "trce: ";

		public const string Dbug = "dbug: ";

		public const string Info = "info: ";

		public const string Warn = "warn: ";

		public const string Fail = "fail: ";

		public const string Crit = "crit: ";
	}

	public static class Hub
	{
		public const int DefaultPort = 8080;

		public const int MaxPort = 65535;

		public const string DefaultHost = "http://localhost:8080";

		public const string RemoteApp = "/hub/mcp-server";

		public const int DefaultTimeoutMs = 60000;
	}

	public const string ApiVersion = "2.0.0";

	public const string PluginVersion = "6.10.0";
}
}
