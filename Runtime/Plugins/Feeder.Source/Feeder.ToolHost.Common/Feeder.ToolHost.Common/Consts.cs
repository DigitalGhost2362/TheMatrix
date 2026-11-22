using Feeder.Json;
using Feeder.Json.Nodes;

namespace Feeder.ToolHost.Common
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
				public const string TrustedInternalClient = "X-ToolHost-Internal-Client";

				public const string TrustedInternalClientOptInValue = "1";
			}

			public const int DefaultIdleTimeoutSeconds = 600;

			public const int DefaultMaxIdleSessionCount = 1000;

			public const string DefaultBodyPath = "mcpServers";

			public const string DefaultServerName = "Feeder-MCP";

			public const string BodyPathDelimiter = "->";

			public static string[] BodyPathSegments(string bodyPath)
			{
				return bodyPath.Split("->");
			}

			public static JsonNode Config(string executablePath, string serverName = "Feeder-MCP", string bodyPath = "mcpServers", int port = 20000, int timeoutMs = 10000)
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

		public const string DefaultHost = "http://localhost:20000";

		public const int DefaultTimeoutMs = 60000;
	}

	public const string ApiVersion = "1.0.0";

}
}
