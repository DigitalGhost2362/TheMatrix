using System.Net;
using Feeder.Matrix;
using Feeder.Matrix.Backends;
using Feeder.Matrix.Compile;
using Feeder.Matrix.Health;
using Feeder.Matrix.Protocol;
using Feeder.Matrix.Tools;
using Feeder.Matrix.Unity;
using Microsoft.AspNetCore.Http.Connections;
using ModelContextProtocol.Protocol;
using Serilog;
using Serilog.Events;

if (StdioMode.ShouldRun(args))
    return await StdioMode.RunAsync(args);

var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FeederMatrix", "logs");
Directory.CreateDirectory(logDir);

// stderr + rolling file only. MCP stdio framing lives in the shim process, but the rule holds
// matrix-wide: nothing is ever written to stdout.
var loggerConfiguration = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.File(Path.Combine(logDir, "matrix-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);

// Unity redirects child stderr while its AppDomain is alive. A domain reload closes that pipe;
// writing to it can terminate an otherwise persistent matrix. The Unity launcher therefore opts
// into file-only logging. Manual matrix launches still get stderr diagnostics.
if (Environment.GetEnvironmentVariable("FEEDER_MATRIX_FILE_LOG_ONLY") != "1")
    loggerConfiguration.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);

Log.Logger = loggerConfiguration.CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.Services.Configure<MatrixOptions>(builder.Configuration.GetSection(MatrixOptions.SectionName));

    var matrixOptions = builder.Configuration.GetSection(MatrixOptions.SectionName).Get<MatrixOptions>() ?? new MatrixOptions();

    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        // Loopback only — never 0.0.0.0 (ADR-0005).
        kestrel.Listen(IPAddress.Loopback, matrixOptions.Port);
        kestrel.Limits.MaxRequestBodySize = matrixOptions.MaxRequestBodyBytes;
    });

    builder.Services.AddSingleton<UnityLinkRegistry>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<McpClientTracker>();
    builder.Services.AddHostedService<McpClientActivityMonitor>();
    builder.Services.AddSingleton<MockUnityBackend>();
    builder.Services.AddSingleton<UnityLinkBackend>();
    builder.Services.AddSingleton<IUnityBackend, BackendRouter>();
    builder.Services.AddSingleton<ScriptCompiler>();
    builder.Services.AddSingleton<EditorHealth>();
    builder.Services.AddSingleton<EditorLock>();
    builder.Services.AddHostedService<EditorLockMonitor>();
    builder.Services.AddSingleton<ServerTools>();

    builder.Services
        .AddSignalR(options => options.MaximumReceiveMessageSize = matrixOptions.MaxRequestBodyBytes)
        .AddMessagePackProtocol();

    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "feeder-mcp-server", Version = MatrixVersion.Value };
            // the Unity tool set changes at runtime (tools switched on or off, editor linked or gone)
            options.Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = true } };
        })
        .WithHttpTransport(options =>
        {
            // Wrap each MCP session so connected AI clients are mirrored to Unity (FMP McpClientsChanged).
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental in the MCP SDK; revisit on SDK upgrades.
            options.RunSessionHandler = (context, server, ct) =>
                context.RequestServices.GetRequiredService<McpClientTracker>().RunSessionAsync(server, ct);
#pragma warning restore MCPEXP002
        })
        .WithListToolsHandler(async (context, ct) =>
        {
            var backend = context.Services!.GetRequiredService<IUnityBackend>();
            var serverTools = context.Services!.GetRequiredService<ServerTools>().List();
            var unityTools = (await backend.ListToolsAsync(ct))
                .Where(tool => serverTools.All(own => own.Name != tool.Name))
                .ToArray();
            context.Services!.GetRequiredService<McpClientTracker>()
                .RecordListed(context.Server.SessionId, unityTools.Select(tool => tool.Name));
            return new ListToolsResult { Tools = [.. serverTools, .. unityTools] };
        })
        .WithCallToolHandler(async (context, ct) =>
        {
            var backend = context.Services!.GetRequiredService<IUnityBackend>();
            var serverTools = context.Services!.GetRequiredService<ServerTools>();
            if (serverTools.Handles(context.Params?.Name))
                return await serverTools.CallAsync(context.Params!, context.Server.SessionId, backend, ct);
            return await serverTools.CallUnityAsync(context.Params!, context.Server.SessionId, backend, ct);
        })
        .WithListPromptsHandler((_, _) => ValueTask.FromResult(new ListPromptsResult()))
        .WithGetPromptHandler((context, _) =>
            ValueTask.FromResult<GetPromptResult>(throwPromptNotFound(context.Params?.Name)))
        .WithListResourcesHandler((_, _) => ValueTask.FromResult(new ListResourcesResult()))
        .WithListResourceTemplatesHandler((_, _) => ValueTask.FromResult(new ListResourceTemplatesResult()))
        .WithReadResourceHandler((context, _) =>
            ValueTask.FromResult<ReadResourceResult>(throwResourceNotFound(context.Params?.Uri)));

    var app = builder.Build();
    var mcpClientTracker = app.Services.GetRequiredService<McpClientTracker>();

    // DNS-rebinding defense: loopback callers must present a loopback Host header (ADR-0005).
    app.Use(async (context, next) =>
    {
        var host = context.Request.Host.Host;
        if (host is not ("127.0.0.1" or "localhost" or "[::1]"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden host.");
            return;
        }

        if (context.Request.Path.StartsWithSegments(FmpConstants.McpPath) &&
            matrixOptions.RequireAuth &&
            !MatrixAuthentication.HasValidBearerToken(context.Request.Headers.Authorization, matrixOptions.Token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized.");
            return;
        }

        if (context.Request.Path.StartsWithSegments(FmpConstants.McpPath) &&
            context.Request.Headers.TryGetValue("Mcp-Session-Id", out var mcpSessionId) &&
            mcpClientTracker.Touch(mcpSessionId.ToString()))
            _ = mcpClientTracker.BroadcastAsync();

        await next();
    });

    app.MapMcp(FmpConstants.McpPath);
    app.MapHub<FmpHub>(FmpConstants.HubPath, o => o.Transports = HttpTransportType.WebSockets);

    // Local health endpoint — intentionally outside the MCP surface.
    app.MapGet("/healthz", (UnityLinkRegistry registry, McpClientTracker mcpClients) => Results.Json(new
    {
        status = "ok",
        version = MatrixVersion.Value,
        mcpClients = mcpClients.Describe().Select(c => new
        {
            c.Info.SessionId,
            c.Info.ClientName,
            c.Info.ClientVersion,
            lastActivity = c.LastActivity.UtcDateTime,
            isActive = c.Info.IsConnected,
        }),
        unityLinks = registry.Links.Select(l => new
        {
            l.ProjectId,
            l.UnityInstanceId,
            editorState = l.EditorState.ToString(),
            l.Connected,
            lastSeen = l.LastSeen.UtcDateTime,
            toolCount = l.Registry?.Tools.Length ?? 0,
        }),
    }));

    Log.Information("Feeder Local Matrix {Version} listening on 127.0.0.1:{Port} (MCP {McpPath}, FMP {HubPath})",
        MatrixVersion.Value, matrixOptions.Port, FmpConstants.McpPath, FmpConstants.HubPath);

    app.Run();
    return 0;
}
catch (Exception e)
{
    Log.Fatal(e, "Matrix terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

static GetPromptResult throwPromptNotFound(string? name)
    => throw new ModelContextProtocol.McpProtocolException($"Unknown prompt: '{name}'", ModelContextProtocol.McpErrorCode.InvalidParams);

static ReadResourceResult throwResourceNotFound(string? uri)
    => throw new ModelContextProtocol.McpProtocolException($"Unknown resource: '{uri}'", ModelContextProtocol.McpErrorCode.InvalidParams);

public static class MatrixVersion
{
    public const string Value = "0.5.0";
}

// Exposes the entry point to WebApplicationFactory-based integration tests.
public partial class Program;
