using System.Threading.Channels;
using Feeder.Matrix.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Feeder.Matrix.CompileTests;

public sealed class EditorLockServerFactory : WebApplicationFactory<Program>;

// the editor's side of the lock, over the hub's JSON protocol the plugin speaks
public sealed class EditorLockHubTests(EditorLockServerFactory factory) : IClassFixture<EditorLockServerFactory>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Editor_that_shows_the_lock_is_told_the_state_on_register()
    {
        await using var link = await ConnectAsync();
        var states = Channel.CreateUnbounded<EditorLockStateMirror>();
        link.On<EditorLockStateMirror>("EditorLockChanged", state => states.Writer.TryWrite(state));

        var response = await RegisterAsync(link, [FmpCapabilities.EditorLock]);
        Assert.Contains(FmpCapabilities.EditorLock, response.Capabilities);
        Assert.False((await states.Reader.ReadAsync().AsTask().WaitAsync(Wait)).Held);
        Assert.Equal("nothing", await link.InvokeAsync<string>("ReleaseEditorLock"));
    }

    [Fact]
    public async Task Unregistered_connection_cannot_free_the_editor()
    {
        await using var link = await ConnectAsync();
        Assert.Equal("nothing", await link.InvokeAsync<string>("ReleaseEditorLock"));
    }

    private async Task<HubConnection> ConnectAsync()
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, FmpConstants.HubPath), HttpTransportType.WebSockets, options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.WebSocketFactory = async (context, ct) => await server.CreateWebSocketClient().ConnectAsync(context.Uri, ct);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static Task<RegisterResponseMirror> RegisterAsync(HubConnection link, string[] features)
    {
        var projectId = Guid.NewGuid().ToString("N");
        return link.InvokeAsync<RegisterResponseMirror>("Register", new RegisterRequestMirror
        {
            ProjectId = projectId,
            ProjectPathHash = projectId,
            UnityInstanceId = Guid.NewGuid().ToString("N"),
            Features = features,
        });
    }
}
