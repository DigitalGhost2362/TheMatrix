using System.Diagnostics;
using System.Text;
using Feeder.StdioShim;

namespace Feeder.Matrix;

/// <summary>
/// Compatibility entry point for stdio MCP clients. The distributed server remains one executable:
/// an stdio invocation relays to a persistent HTTP matrix and starts a matrix-mode sibling process
/// when Unity has not already done so.
/// </summary>
public static class StdioMode
{
    public static bool ShouldRun(string[] args)
        => args.Any(a => a.Equals("--stdio", StringComparison.OrdinalIgnoreCase) ||
                         a.Equals("client-transport=stdio", StringComparison.OrdinalIgnoreCase) ||
                         a.Equals("--client-transport=stdio", StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(string[] args)
    {
        var port = ReadInt(args, "port", Protocol.FmpConstants.DefaultPort);
        var url = ReadValue(args, "url") ?? $"http://127.0.0.1:{port}{Protocol.FmpConstants.McpPath}";
        var token = ReadValue(args, "token");
        var requireAuth = string.Equals(ReadValue(args, "authorization"), "required", StringComparison.OrdinalIgnoreCase) ||
                          ReadBool(args, "require-auth");

        using var stdout = Console.OpenStandardOutput();
        var stdoutLock = new SemaphoreSlim(1, 1);

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

        using var relay = new MatrixRelay(
            url,
            WriteLineToStdoutAsync,
            message => Console.Error.WriteLine($"[feeder-mcp-server] {message}"),
            requireAuth ? token : null,
            () => TryStartMatrix(port, requireAuth, token));

        if (!await relay.WaitForMatrixAsync(TimeSpan.FromMilliseconds(500)))
        {
            if (!TryStartMatrix(port, requireAuth, token) ||
                !await relay.WaitForMatrixAsync(TimeSpan.FromSeconds(10)))
            {
                Console.Error.WriteLine($"[feeder-mcp-server] matrix unreachable at {url} and could not be started.");
                return 1;
            }
        }

        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin, Encoding.UTF8);
        await relay.RunAsync(reader, CancellationToken.None);

        return 0;
    }

    private static bool TryStartMatrix(int port, bool requireAuth, string? token)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
            return false;

        try
        {
            // the matrix outlives this agent: it must not inherit the agent's stdio pipes or log into them
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.Environment["FEEDER_MATRIX_FILE_LOG_ONLY"] = "1";
            startInfo.ArgumentList.Add($"--FeederMatrix:Port={port}");
            startInfo.ArgumentList.Add($"--FeederMatrix:RequireAuth={requireAuth}");
            if (!string.IsNullOrEmpty(token))
                startInfo.ArgumentList.Add($"--FeederMatrix:Token={token}");
            using var process = Process.Start(startInfo);
            if (process == null)
                return false;
            process.StandardInput.Close();
            process.StandardOutput.Close();
            process.StandardError.Close();
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[feeder-mcp-server] failed to start matrix: {e.Message}");
            return false;
        }
    }

    private static string? ReadValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            foreach (var prefix in new[] { key + "=", "--" + key + "=" })
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arg[prefix.Length..];

            if (arg.Equals("--" + key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
    }

    private static int ReadInt(string[] args, string key, int fallback)
        => int.TryParse(ReadValue(args, key), out var value) ? value : fallback;

    private static bool ReadBool(string[] args, string key)
        => bool.TryParse(ReadValue(args, key), out var value) && value;
}
