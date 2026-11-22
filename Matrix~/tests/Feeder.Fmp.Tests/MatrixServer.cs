using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Feeder.Fmp.Tests
{
    // runs the packaged matrix-server on a free loopback port; only ever kills the process it started
    internal sealed class MatrixServer : IDisposable
    {
        static readonly object Sync = new object();
        static readonly List<Process> Started = new List<Process>();
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        static string _exe;

        readonly StringBuilder _output = new StringBuilder();

        MatrixServer(int port)
        {
            Port = port;
        }

        static MatrixServer()
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                lock (Sync)
                {
                    foreach (Process process in Started)
                    {
                        try { if (!process.HasExited) process.Kill(true); } catch (Exception) { }
                    }
                }
            };
        }

        public int Port { get; }

        public Process Process { get; private set; }

        public Uri HubUrl => new Uri("ws://127.0.0.1:" + Port + "/fmp");

        public string McpUrl => "http://127.0.0.1:" + Port + "/mcp";

        public string HealthUrl => "http://127.0.0.1:" + Port + "/healthz";

        public string Output
        {
            get
            {
                lock (_output)
                    return _output.ToString();
            }
        }

        public bool HasExited => Process.HasExited;

        // port 0 asks the os for an ephemeral port, which is never in the 20000-29999 range the editor derives its own from
        public static async Task<MatrixServer> StartAsync(int port = 0, TimeSpan? toolTimeout = null, string extraArguments = null)
        {
            if (port == 0)
                port = FreePort();
            var server = new MatrixServer(port);
            string exe = ResolveExe();
            var info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--FeederMatrix:Port=" + port +
                            " --FeederMatrix:DefaultToolTimeout=" + (toolTimeout ?? TimeSpan.FromSeconds(30)).ToString("c") +
                            " --FeederMatrix:RequireAuth=false" +
                            (extraArguments == null ? string.Empty : " " + extraArguments),
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (sender, args) => server.Append(args.Data);
            process.ErrorDataReceived += (sender, args) => server.Append(args.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (Sync)
                Started.Add(process);
            server.Process = process;
            await server.WaitUntilReadyAsync().ConfigureAwait(false);
            return server;
        }

        public void Kill()
        {
            if (Process == null)
                return;
            try
            {
                if (!Process.HasExited)
                    Process.Kill(true);
                Process.WaitForExit(10000);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose() => Kill();

        void Append(string line)
        {
            if (line == null)
                return;
            lock (_output)
                _output.AppendLine(line);
        }

        async Task WaitUntilReadyAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (Process.HasExited)
                    throw new InvalidOperationException("The matrix server exited with code " + Process.ExitCode + " before it was ready:\n" + Output);
                try
                {
                    using (HttpResponseMessage response = await Http.GetAsync(HealthUrl).ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode)
                            return;
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            Kill();
            throw new TimeoutException("The matrix server was not ready after 30 seconds:\n" + Output);
        }

        public async Task<string> HealthAsync()
        {
            using (HttpResponseMessage response = await Http.GetAsync(HealthUrl).ConfigureAwait(false))
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        static string ResolveExe()
        {
            lock (Sync)
            {
                if (_exe != null)
                    return _exe;
                string given = Environment.GetEnvironmentVariable("FEEDER_FMP_SERVER_EXE");
                if (!string.IsNullOrEmpty(given))
                {
                    if (!File.Exists(given))
                        throw new FileNotFoundException("FEEDER_FMP_SERVER_EXE does not point at a file.", given);
                    return _exe = given;
                }
                string zip = FindPayload();
                var info = new FileInfo(zip);
                string folder = Path.Combine(Path.GetTempPath(), "feeder-fmp-tests", info.Length + "-" + info.LastWriteTimeUtc.Ticks);
                string exe = Path.Combine(folder, "matrix-server.exe");
                if (!File.Exists(exe))
                {
                    string staging = folder + "." + Guid.NewGuid().ToString("N");
                    ZipFile.ExtractToDirectory(zip, staging);
                    try { Directory.Move(staging, folder); } catch (IOException) { Directory.Delete(staging, true); }
                }
                return _exe = exe;
            }
        }

        static string FindPayload()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "Server~", "win-x64", "server-payload.zip");
                if (File.Exists(candidate))
                    return candidate;
            }
            throw new FileNotFoundException("Server~/win-x64/server-payload.zip was not found above the test output; set FEEDER_FMP_SERVER_EXE.");
        }
    }
}
