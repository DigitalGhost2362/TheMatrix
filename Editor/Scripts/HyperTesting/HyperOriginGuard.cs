#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Feeder.MCP.Editor.HyperTesting
{
    // a clone reaches the original's Assets through a junction and asset creation cannot be cancelled,
    // so git status of the original is compared before the clones start and after they run
    public static class HyperOriginGuard
    {
        public readonly struct Result
        {
            public Result(bool baselineKnown, IReadOnlyList<string> newPaths, string note)
            {
                BaselineKnown = baselineKnown;
                NewPaths = newPaths;
                Note = note;
            }

            public bool BaselineKnown { get; }
            public IReadOnlyList<string> NewPaths { get; }
            public string Note { get; }
        }

        const int GitTimeoutMs = 30000;
        static readonly object _lock = new object();
        static DateTime _checkedUtc = DateTime.MinValue;
        static Result _last = new Result(false, Array.Empty<string>(), string.Empty);

        // empty when the baseline was written, otherwise why it could not be
        public static string TakeBaseline(string originRoot)
        {
            var current = GitStatus(originRoot, out var error);
            if (current == null)
            {
                // an old baseline would report every file made since the last run
                if (File.Exists(HyperTestingPaths.GitBaselineFile))
                    File.Delete(HyperTestingPaths.GitBaselineFile);
                return $"git baseline not taken ({error}); new files written into the original cannot be detected this run";
            }
            Directory.CreateDirectory(Path.GetDirectoryName(HyperTestingPaths.GitBaselineFile)!);
            File.WriteAllText(HyperTestingPaths.GitBaselineFile, string.Join("\n", current));
            lock (_lock)
            {
                _checkedUtc = DateTime.MinValue;
                _last = new Result(true, Array.Empty<string>(), string.Empty);
            }
            return string.Empty;
        }

        // maxAgeSec keeps a status poll from running git every time; 0 forces a fresh run
        public static Result Check(string originRoot, int maxAgeSec)
        {
            lock (_lock)
            {
                if (maxAgeSec > 0 && (DateTime.UtcNow - _checkedUtc).TotalSeconds < maxAgeSec)
                    return _last;
            }
            if (!File.Exists(HyperTestingPaths.GitBaselineFile))
                return Remember(new Result(false, Array.Empty<string>(), "no git baseline yet (it is taken by the next up)"));
            var current = GitStatus(originRoot, out var error);
            if (current == null)
                return Remember(new Result(false, Array.Empty<string>(), $"git status failed ({error})"));
            var baseline = File.ReadAllText(HyperTestingPaths.GitBaselineFile).Split('\n');
            return Remember(new Result(true, NewUntracked(baseline, current), string.Empty));
        }

        internal static List<string> NewUntracked(IEnumerable<string> baseline, IEnumerable<string> current)
        {
            var known = new HashSet<string>(baseline);
            return current.Where(line => line.StartsWith("?? ", StringComparison.Ordinal) && !known.Contains(line)).Select(line => line.Substring(3)).ToList();
        }

        static Result Remember(Result result)
        {
            lock (_lock)
            {
                _checkedUtc = DateTime.UtcNow;
                _last = result;
            }
            return result;
        }

        static List<string>? GitStatus(string root, out string error)
        {
            error = string.Empty;
            try
            {
                var info = new ProcessStartInfo("git", $"-C \"{root}\" status --porcelain -z --untracked-files=all")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                using var process = Process.Start(info)!;
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(GitTimeoutMs))
                {
                    try { process.Kill(); }
                    catch (Exception) { }
                    error = $"timed out after {GitTimeoutMs / 1000} s";
                    return null;
                }
                if (process.ExitCode != 0)
                {
                    error = stderr.Result.Trim();
                    return null;
                }
                return stdout.Result.Split('\0').Where(line => line.Length > 0).ToList();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
    }
}
