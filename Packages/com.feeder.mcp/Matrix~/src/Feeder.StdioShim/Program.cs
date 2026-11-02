using System.Diagnostics;
using System.Text;
using Feeder.StdioShim;

// Feeder stdio shim: relays newline-delimited JSON-RPC between an stdio-only MCP client and the
// persistent Feeder Local Matrix (Streamable HTTP). Zero business logic; all diagnostics go to
// stderr — stdout carries exclusively JSON-RPC.
//
//   feeder-stdio-shim [--url http://127.0.0.1:20270/mcp] [--matrix-exe <path-to-feeder-matrix.exe>]

var url = "http://127.0.0.1:20270/mcp";
string? matrixExe = Environment.GetEnvironmentVariable("FEEDER_MATRIX_EXE");
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--url") url = args[i + 1];
    if (args[i] == "--matrix-exe") matrixExe = args[i + 1];
}

using var stdout = Console.OpenStandardOutput();
var stdoutLock = new SemaphoreSlim(1, 1);
using var shutdown = new CancellationTokenSource();

async Task WriteLineToStdoutAsync(string json)
{
    var bytes = Encoding.UTF8.GetBytes(json + "\n");
    await stdoutLock.WaitAsync();
    try
    {
        await stdout.WriteAsync(bytes);
        await stdout.FlushAsync();
    }
    finally
    {
        stdoutLock.Release();
    }
}

using var relay = new MatrixRelay(url, WriteLineToStdoutAsync, msg => Console.Error.WriteLine($"[feeder-stdio-shim] {msg}"),
    startMatrix: () => TryStartMatrix(matrixExe));

if (!await relay.WaitForMatrixAsync(TimeSpan.FromMilliseconds(500)))
{
    if (!TryStartMatrix(matrixExe) || !await relay.WaitForMatrixAsync(TimeSpan.FromSeconds(10)))
    {
        Console.Error.WriteLine($"[feeder-stdio-shim] matrix unreachable at {url} and could not be started.");
        return 1;
    }
}

using var stdin = Console.OpenStandardInput();
using var reader = new StreamReader(stdin, Encoding.UTF8);

await relay.RunAsync(reader, shutdown.Token);

// stdin closed → client is gone; drop the session and exit.
shutdown.Cancel();
return 0;

static bool TryStartMatrix(string? matrixExe)
{
    matrixExe ??= Path.Combine(AppContext.BaseDirectory, "feeder-matrix.exe");
    if (!File.Exists(matrixExe))
    {
        Console.Error.WriteLine($"[feeder-stdio-shim] matrix executable not found: {matrixExe}");
        return false;
    }
    try
    {
        // the matrix outlives this agent: it must not inherit the agent's stdio pipes or log into them
        var startInfo = new ProcessStartInfo(matrixExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(matrixExe)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["FEEDER_MATRIX_FILE_LOG_ONLY"] = "1";
        using var process = Process.Start(startInfo);
        if (process == null)
            return false;
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
        Console.Error.WriteLine($"[feeder-stdio-shim] started matrix: {matrixExe}");
        return true;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"[feeder-stdio-shim] failed to start matrix: {e.Message}");
        return false;
    }
}
