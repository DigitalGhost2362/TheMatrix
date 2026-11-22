using System.Reflection;
using Feeder.Matrix.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Feeder.Matrix.CompileTests;

public sealed class CompileServerFactory : WebApplicationFactory<Program>;

public sealed class FmpHubCompileTests(CompileServerFactory factory) : IClassFixture<CompileServerFactory>
{
    [Fact]
    public async Task Register_advertises_compile()
    {
        await using var link = await ConnectAsync();

        var response = await RegisterAsync(link);

        Assert.NotEmpty(response.SessionId);
        Assert.Contains(FmpCapabilities.Compile, response.Capabilities);
    }

    [Fact]
    public async Task Compile_without_register_is_refused()
    {
        await using var link = await ConnectAsync();

        var response = await CompileAsync(link, "public static class Script { public static int Main() => 1; }");

        Assert.False(response.Success);
        Assert.Null(response.Assembly);
        Assert.Contains("Register", response.Error);
    }

    [Fact]
    public async Task Registered_link_gets_an_assembly_it_can_run()
    {
        await using var link = await ConnectAsync();
        await RegisterAsync(link);

        var response = await CompileAsync(link, "public static class Script { public static int Main(int x) => x * 2; }");

        Assert.True(response.Success, string.Join("\n", response.Diagnostics));
        var main = Assembly.Load(response.Assembly!).GetType("Script", throwOnError: true)!.GetMethod("Main")!;
        Assert.Equal(42, main.Invoke(null, [21]));
    }

    [Fact]
    public async Task Failure_diagnostics_cross_the_wire_unchanged()
    {
        await using var link = await ConnectAsync();
        await RegisterAsync(link);

        var failed = await CompileAsync(link, "public class Script\n{\n    public static void Main()\n    {\n        var y = missing + 1;\n    }\n}\n");
        var syntax = await CompileAsync(link, "public class Script { public static void Main() { int a = 1 } }", syntaxOnly: true);

        Assert.False(failed.Success);
        Assert.Null(failed.Error);
        Assert.Equal(new[] { "(5,17): error CS0103: The name 'missing' does not exist in the current context" }, failed.Diagnostics);
        Assert.False(syntax.Success);
        Assert.Null(syntax.Assembly);
        Assert.Equal(new[] { "(1,61): error CS1002: ; expected" }, syntax.Diagnostics);
    }

    [Fact]
    public async Task Concurrent_compiles_all_complete()
    {
        await using var first = await ConnectAsync();
        await using var second = await ConnectAsync();
        await RegisterAsync(first);
        await RegisterAsync(second);

        var results = await Task.WhenAll(
            CompileAsync(first, "public static class Script { public static int Main() => 1; }"),
            CompileAsync(second, "public static class Script { public static int Main() => 2; }"),
            CompileAsync(first, "public static class Script { public static int Main() => 3; }"));

        Assert.All(results, r => Assert.True(r.Success, string.Join("\n", r.Diagnostics)));
        Assert.Equal(new object[] { 1, 2, 3 }, results.Select(r => Assembly.Load(r.Assembly!).GetType("Script", throwOnError: true)!.GetMethod("Main")!.Invoke(null, null)));
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

    private static Task<RegisterResponseMirror> RegisterAsync(HubConnection link)
    {
        var projectId = Guid.NewGuid().ToString("N");
        return link.InvokeAsync<RegisterResponseMirror>("Register", new RegisterRequestMirror
        {
            ProjectId = projectId,
            ProjectPathHash = projectId,
            UnityInstanceId = Guid.NewGuid().ToString("N"),
        });
    }

    private static Task<CompileResponseMirror> CompileAsync(HubConnection link, string source, bool syntaxOnly = false)
        => link.InvokeAsync<CompileResponseMirror>("Compile", new CompileRequestMirror
        {
            Source = source,
            ReferencePaths = References.Core,
            SyntaxOnly = syntaxOnly,
        });
}
