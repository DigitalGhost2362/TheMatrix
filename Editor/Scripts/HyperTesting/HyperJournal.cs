#nullable enable
using System;
using System.IO;

namespace Feeder.MCP.Editor.HyperTesting
{
    public static class HyperJournal
    {
        static readonly object _lock = new object();

        public static void AppendAuto(string file, string line)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.AppendAllText(file, $"\n- {DateTime.Now:yyyy-MM-dd HH:mm:ss} [auto] {line.Replace('\n', ' ')}\n");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"{HyperTestingActivation.LogPrefix} could not write journal '{file}': {ex.Message}");
            }
        }
    }
}
