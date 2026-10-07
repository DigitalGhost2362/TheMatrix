using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Feeder.Fmp.Tests
{
    internal sealed class ToolCall
    {
        public string OperationId { get; set; }

        public string ToolName { get; set; }

        public string ArgumentsJson { get; set; }
    }

    // plays the Unity editor against the real matrix: registers, publishes tools, answers InvokeTool
    internal sealed class UnitySide : IDisposable
    {
        public const string DefaultProjectId = "fmp-tests";

        readonly List<FmpState> _states = new List<FmpState>();
        readonly List<string> _clientLists = new List<string>();

        public UnitySide(Uri url, FmpOptions options = null, string projectId = DefaultProjectId)
        {
            ProjectId = projectId;
            Connection = new FmpConnection(url, options);
            Connection.StateChanged += state => { lock (_states) _states.Add(state); };
            Connection.On("McpClientsChanged", arguments => { lock (_clientLists) _clientLists.Add(arguments); return Task.CompletedTask; });
            Connection.On("InvokeTool", HandleInvokeAsync);
            Connection.On("CancelTool", arguments => OnCancel == null ? Task.CompletedTask : OnCancel(arguments));
        }

        public FmpConnection Connection { get; }

        public string ProjectId { get; }

        public int LastInvokeChars { get; private set; }

        public long LastCompleteMs { get; private set; }

        public string InstanceId { get; } = Guid.NewGuid().ToString("N");

        // returns the CallToolResult json that goes back to the AI client
        public Func<ToolCall, Task<string>> OnTool { get; set; }

        public Func<string, Task> OnCancel { get; set; }

        public FmpState[] States
        {
            get
            {
                lock (_states)
                    return _states.ToArray();
            }
        }

        public string[] ClientLists
        {
            get
            {
                lock (_clientLists)
                    return _clientLists.ToArray();
            }
        }

        public Task StartAsync() => Connection.StartAsync();

        public Task<string> RegisterAsync(string registryHash = "none")
            => Connection.InvokeAsync("Register",
                "[{\"protocolVersion\":\"1\",\"projectId\":\"" + ProjectId + "\",\"unityInstanceId\":\"" + InstanceId +
                "\",\"projectPathHash\":\"h\",\"unityVersion\":\"2022.3.62f3\",\"pluginVersion\":\"0.0.0-test\"," +
                "\"supportedProtocolVersions\":[\"1\"],\"toolRegistryHash\":\"" + registryHash +
                "\",\"authToken\":\"\",\"editorState\":0,\"pendingOperations\":[]}]");

        public Task PublishToolsAsync(params string[] names)
        {
            string schema = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}}}"));
            string tools = string.Join(",", names.Select(name =>
                "{\"name\":\"" + name + "\",\"title\":\"" + name + " title\",\"description\":\"" + name + " description\"," +
                "\"inputSchemaJsonUtf8\":\"" + schema + "\",\"readOnly\":true,\"destructive\":false,\"idempotent\":true,\"threadSafe\":false}"));
            return Connection.SendAsync("PublishRegistry",
                "[{\"projectId\":\"" + ProjectId + "\",\"registryHash\":\"registry-" + names.Length + "\",\"tools\":[" + tools + "]}]");
        }

        public static string TextResult(string text, bool isError = false)
            => "{\"isError\":" + (isError ? "true" : "false") + ",\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(text, Json.Relaxed) + "}]}";

        public void Dispose() => Connection.Dispose();

        async Task HandleInvokeAsync(string arguments)
        {
            ToolCall call;
            using (JsonDocument document = JsonDocument.Parse(arguments))
            {
                JsonElement invoke = document.RootElement[0];
                call = new ToolCall
                {
                    OperationId = invoke.GetProperty("operationId").GetString(),
                    ToolName = invoke.GetProperty("toolName").GetString(),
                    ArgumentsJson = Encoding.UTF8.GetString(Convert.FromBase64String(invoke.GetProperty("argumentsJsonUtf8").GetString())),
                };
            }
            LastInvokeChars = arguments.Length;
            string content = await OnTool(call).ConfigureAwait(false);
            string complete = "[{\"operationId\":\"" + call.OperationId + "\",\"success\":true,\"contentJsonUtf8\":\"" +
                              Convert.ToBase64String(Encoding.UTF8.GetBytes(content)) + "\"}]";
            var watch = Stopwatch.StartNew();
            await Connection.SendAsync("CompleteTool", complete).ConfigureAwait(false);
            LastCompleteMs = watch.ElapsedMilliseconds;
        }
    }
}
