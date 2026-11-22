using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Feeder.Fmp.Tests
{
    // black-box: deadlines that survive a reload, move on request and stop when the editor is gone
    public sealed class ReliabilityTests
    {
        static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);
        const string ProjectRoot = "C:/no-such-project";

        static async Task<MatrixServer> StartAsync(TimeSpan toolTimeout, TimeSpan grace)
            => await MatrixServer.StartAsync(toolTimeout: toolTimeout,
                extraArguments: "--FeederMatrix:ReconnectGrace=" + grace.ToString("c"));

        static Task ProgressAsync(UnitySide unity, string operationId, bool deferred = false, long extendMs = 0)
            => unity.Connection.SendAsync("ReportProgress",
                "[{\"operationId\":\"" + operationId + "\",\"deferred\":" + (deferred ? "true" : "false") + ",\"extendDeadlineMs\":" + extendMs + "}]");

        static Task CompleteAsync(UnitySide unity, string operationId, string text)
            => unity.Connection.SendAsync("CompleteTool",
                "[{\"operationId\":\"" + operationId + "\",\"success\":true,\"contentJsonUtf8\":\"" +
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(UnitySide.TextResult(text))) + "\"}]");

        static async Task<McpHttpClient> McpWithToolAsync(MatrixServer server, string tool)
        {
            var mcp = new McpHttpClient(server.McpUrl);
            await mcp.InitializeAsync();
            for (int i = 0; i < 100; i++)
            {
                using (JsonDocument list = await mcp.ListToolsAsync())
                {
                    if (Array.IndexOf(McpHttpClient.ToolNames(list), tool) >= 0)
                        return mcp;
                }
                await Task.Delay(50);
            }
            throw new TimeoutException("tools/list never showed '" + tool + "'.");
        }

        [Fact]
        public async Task Deferred_call_outlives_the_default_timeout_and_completes_after_a_reconnect()
        {
            using (MatrixServer server = await StartAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30)))
            {
                var invoked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = new UnitySide(server.HubUrl);
                first.OnTool = async call =>
                {
                    await ProgressAsync(first, call.OperationId, deferred: true);
                    invoked.TrySetResult(call.OperationId);
                    await Task.Delay(Timeout.Infinite);
                    return null;
                };
                await first.StartAsync();
                await first.RegisterAsync(projectRoot: ProjectRoot);
                await first.PublishToolsAsync("refresh");
                await using (McpHttpClient mcp = await McpWithToolAsync(server, "refresh"))
                {
                    var watch = Stopwatch.StartNew();
                    Task<JsonDocument> call = mcp.CallToolAsync("refresh", "{}");
                    string operationId = await invoked.Task.WaitAsync(Limit);

                    // the domain reload: the link drops, a new domain registers with the same instance id
                    first.Dispose();
                    await Task.Delay(3000);
                    using (var second = new UnitySide(server.HubUrl, instanceId: first.InstanceId))
                    {
                        await second.StartAsync();
                        await second.RegisterAsync("registry-1", ProjectRoot);
                        await Task.Delay(1000);
                        await CompleteAsync(second, operationId, "compiled");
                        using (JsonDocument response = await call.WaitAsync(Limit))
                            Assert.Equal("compiled", McpHttpClient.FirstText(response));
                    }
                    Assert.True(watch.Elapsed > TimeSpan.FromSeconds(3), "the call should have outlived the 2 s default timeout");
                }
            }
        }

        [Fact]
        public async Task Operation_lost_in_a_reload_fails_at_once_with_a_clear_message()
        {
            using (MatrixServer server = await StartAsync(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)))
            {
                var invoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = new UnitySide(server.HubUrl);
                first.OnTool = async call =>
                {
                    invoked.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite);
                    return null;
                };
                await first.StartAsync();
                await first.RegisterAsync(projectRoot: ProjectRoot);
                await first.PublishToolsAsync("run");
                await using (McpHttpClient mcp = await McpWithToolAsync(server, "run"))
                {
                    Task<JsonDocument> call = mcp.CallToolAsync("run", "{}");
                    await invoked.Task.WaitAsync(Limit);
                    first.Dispose();
                    using (var second = new UnitySide(server.HubUrl, instanceId: first.InstanceId))
                    {
                        await second.StartAsync();
                        var watch = Stopwatch.StartNew();
                        await second.RegisterAsync("registry-1", ProjectRoot);
                        using (JsonDocument response = await call.WaitAsync(TimeSpan.FromSeconds(10)))
                        {
                            Assert.Contains("cut off by a Unity domain reload", response.RootElement.GetRawText());
                            Assert.Contains("FMP_INTERRUPTED", response.RootElement.GetRawText());
                        }
                        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
                    }
                }
            }
        }

        [Fact]
        public async Task Call_stops_waiting_when_the_editor_does_not_come_back()
        {
            using (MatrixServer server = await StartAsync(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2)))
            {
                var invoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var unity = new UnitySide(server.HubUrl);
                unity.OnTool = async call =>
                {
                    invoked.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite);
                    return null;
                };
                await unity.StartAsync();
                await unity.RegisterAsync(projectRoot: ProjectRoot);
                await unity.PublishToolsAsync("run");
                await using (McpHttpClient mcp = await McpWithToolAsync(server, "run"))
                {
                    var watch = Stopwatch.StartNew();
                    Task<JsonDocument> call = mcp.CallToolAsync("run", "{}");
                    await invoked.Task.WaitAsync(Limit);
                    unity.Dispose();
                    using (JsonDocument response = await call.WaitAsync(Limit))
                        Assert.Contains("did not come back", response.RootElement.GetRawText());
                    Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));

                    // past the grace the editor's tools leave the list and calls fail at once
                    using (JsonDocument list = await mcp.ListToolsAsync())
                        Assert.Equal(new[] { "editor-health", "editor-lock-acquire", "editor-lock-release", "editor-lock-status", "tool-call" }, McpHttpClient.ToolNames(list));
                    using (JsonDocument response = await mcp.CallToolAsync("run", "{}").WaitAsync(TimeSpan.FromSeconds(5)))
                        Assert.Contains("No Unity editor is connected", response.RootElement.GetRawText());
                }
            }
        }

        [Fact]
        public async Task Tool_can_ask_for_more_time()
        {
            using (MatrixServer server = await StartAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30)))
            using (var unity = new UnitySide(server.HubUrl))
            {
                unity.OnTool = async call =>
                {
                    await ProgressAsync(unity, call.OperationId, extendMs: 6000);
                    await Task.Delay(3500);
                    return UnitySide.TextResult("slow but fine");
                };
                await unity.StartAsync();
                await unity.RegisterAsync(projectRoot: ProjectRoot);
                await unity.PublishToolsAsync("bake");
                await using (McpHttpClient mcp = await McpWithToolAsync(server, "bake"))
                using (JsonDocument response = await mcp.CallToolAsync("bake", "{}").WaitAsync(Limit))
                    Assert.Equal("slow but fine", McpHttpClient.FirstText(response));
            }
        }

        [Fact]
        public async Task Timeout_without_more_time_reports_editor_health()
        {
            using (MatrixServer server = await StartAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30)))
            using (var unity = new UnitySide(server.HubUrl))
            {
                unity.OnTool = async call =>
                {
                    await Task.Delay(4000);
                    return UnitySide.TextResult("too late");
                };
                await unity.StartAsync();
                await unity.RegisterAsync(projectRoot: ProjectRoot);
                await unity.PublishToolsAsync("slow");
                await using (McpHttpClient mcp = await McpWithToolAsync(server, "slow"))
                using (JsonDocument response = await mcp.CallToolAsync("slow", "{}").WaitAsync(Limit))
                {
                    string text = response.RootElement.GetRawText();
                    Assert.Contains("timed out", text);
                    Assert.Contains("Editor health", text);
                }
            }
        }
    }
}
