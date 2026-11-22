using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Feeder.Matrix.Benchmarks;

// feeder-matrix-bench baseline --url http://127.0.0.1:20261/mcp --label previous-local --out baseline.json
//   [--pings 10000] [--tools-list 1000] [--tool-pings 500] [--session-inits 50] [--pid <serverPid>] [--export <dir>]
var options = ParseArgs(args);
if (options is null)
{
    Console.Error.WriteLine("usage: feeder-matrix-bench baseline --url <mcp-url> --label <label> --out <file.json> [--pings N] [--tools-list N] [--tool-pings N] [--session-inits N] [--pid PID] [--export DIR]");
    return 1;
}

var report = new JsonObject
{
    ["label"] = options.Label,
    ["url"] = options.Url,
    ["timestampUtc"] = DateTime.UtcNow.ToString("o"),
    ["machine"] = Environment.MachineName,
    ["os"] = Environment.OSVersion.ToString(),
    ["harness"] = "feeder-matrix-bench/0.1.0",
};

using (var client = new McpHttpBenchClient(options.Url))
{
    var initElapsed = await client.InitializeAsync();
    report["serverInfo"] = JsonNode.Parse(client.ServerInfoJson!);
    report["protocolVersion"] = client.NegotiatedProtocolVersion;
    Console.Error.WriteLine($"initialized against {client.ServerInfoJson} in {initElapsed.TotalMilliseconds:F2} ms");

    var cases = new JsonObject();
    report["cases"] = cases;

    // Session initialize latency (new session per iteration). NOTE: this measures MCP session setup,
    // not server process cold start — restarting the server under test is out of scope for the harness.
    cases["session_initialize"] = await RunCaseAsync(options.SessionInits, async _ =>
    {
        using var c = new McpHttpBenchClient(options.Url);
        return await c.InitializeAsync();
    });
    Console.Error.WriteLine("session_initialize done");

    // Warm protocol-level ping (server answers without touching Unity).
    cases["ping_warm"] = await RunCaseAsync(options.Pings, async _ => (await client.RequestAsync("ping", null)).elapsed);
    Console.Error.WriteLine("ping_warm done");

    // tools/list (schema-heavy control-plane request).
    long toolsListBytes = 0;
    cases["tools_list"] = await RunCaseAsync(options.ToolsList, async _ =>
    {
        var (elapsed, json) = await client.RequestAsync("tools/list", null);
        toolsListBytes = json.Length;
        return elapsed;
    });
    cases["tools_list"]!["responseBytes"] = toolsListBytes;
    Console.Error.WriteLine("tools_list done");

    // End-to-end Unity round trip via the `ping` tool (AI edge -> server -> Unity -> back).
    cases["tool_call_ping_e2e"] = await RunCaseAsync(options.ToolPings, async _ =>
        (await client.RequestAsync("tools/call", """{"name":"ping","arguments":{}}""")).elapsed);
    Console.Error.WriteLine("tool_call_ping_e2e done");

    if (options.Pid is int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            report["serverProcess"] = new JsonObject
            {
                ["pid"] = pid,
                ["name"] = p.ProcessName,
                ["workingSetMb"] = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1),
                ["privateMb"] = Math.Round(p.PrivateMemorySize64 / 1024.0 / 1024.0, 1),
                ["totalCpuSeconds"] = Math.Round(p.TotalProcessorTime.TotalSeconds, 1),
                ["startTimeUtc"] = p.StartTime.ToUniversalTime().ToString("o"),
            };
        }
        catch (Exception e)
        {
            report["serverProcess"] = new JsonObject { ["error"] = e.Message };
        }
    }

    if (options.ExportDir is string exportDir)
    {
        Directory.CreateDirectory(exportDir);
        await ExportAsync(client, exportDir, "tools-list.json", "tools/list");
        await ExportAsync(client, exportDir, "prompts-list.json", "prompts/list");
        await ExportAsync(client, exportDir, "resources-list.json", "resources/list");
        await ExportAsync(client, exportDir, "resource-templates-list.json", "resources/templates/list");
        Console.Error.WriteLine($"definitions exported to {exportDir}");
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.Out))!);
await File.WriteAllTextAsync(options.Out, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
Console.Error.WriteLine($"report written to {options.Out}");
return 0;

static async Task<JsonObject> RunCaseAsync(int iterations, Func<int, Task<TimeSpan>> action)
{
    var samples = new double[iterations];
    var errors = 0;
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
    {
        try
        {
            samples[i] = (await action(i)).TotalMilliseconds;
        }
        catch
        {
            samples[i] = double.NaN;
            errors++;
        }
    }
    sw.Stop();

    var ok = samples.Where(s => !double.IsNaN(s)).OrderBy(s => s).ToArray();
    return new JsonObject
    {
        ["iterations"] = iterations,
        ["errors"] = errors,
        ["totalSeconds"] = Math.Round(sw.Elapsed.TotalSeconds, 2),
        ["p50Ms"] = Percentile(ok, 50),
        ["p95Ms"] = Percentile(ok, 95),
        ["p99Ms"] = Percentile(ok, 99),
        ["minMs"] = ok.Length > 0 ? Math.Round(ok[0], 3) : null,
        ["maxMs"] = ok.Length > 0 ? Math.Round(ok[^1], 3) : null,
        ["meanMs"] = ok.Length > 0 ? Math.Round(ok.Average(), 3) : null,
    };
}

static double? Percentile(double[] sorted, double p)
{
    if (sorted.Length == 0) return null;
    var index = (int)Math.Ceiling(p / 100.0 * sorted.Length) - 1;
    return Math.Round(sorted[Math.Clamp(index, 0, sorted.Length - 1)], 3);
}

static async Task ExportAsync(McpHttpBenchClient client, string dir, string fileName, string method)
{
    try
    {
        var (_, json) = await client.RequestAsync(method, null);
        var node = JsonNode.Parse(json)!;
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception e)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), JsonSerializer.Serialize(new { error = e.Message }));
    }
}

static Options? ParseArgs(string[] args)
{
    if (args.Length == 0 || args[0] != "baseline") return null;
    string? url = null, label = null, @out = null, export = null;
    int pings = 10_000, toolsList = 1_000, toolPings = 500, sessionInits = 50;
    int? pid = null;
    for (var i = 1; i < args.Length - 1; i += 2)
    {
        switch (args[i])
        {
            case "--url": url = args[i + 1]; break;
            case "--label": label = args[i + 1]; break;
            case "--out": @out = args[i + 1]; break;
            case "--export": export = args[i + 1]; break;
            case "--pings": pings = int.Parse(args[i + 1]); break;
            case "--tools-list": toolsList = int.Parse(args[i + 1]); break;
            case "--tool-pings": toolPings = int.Parse(args[i + 1]); break;
            case "--session-inits": sessionInits = int.Parse(args[i + 1]); break;
            case "--pid": pid = int.Parse(args[i + 1]); break;
            default: return null;
        }
    }
    if (url is null || label is null || @out is null) return null;
    return new Options(url, label, @out, export, pings, toolsList, toolPings, sessionInits, pid);
}

internal sealed record Options(
    string Url, string Label, string Out, string? ExportDir,
    int Pings, int ToolsList, int ToolPings, int SessionInits, int? Pid);
