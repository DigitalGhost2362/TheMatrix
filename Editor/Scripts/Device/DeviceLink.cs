#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Feeder.MCP.Editor.Device
{
    // calls a Runtime tool of the linked device build through the Matrix server
    internal static class DeviceLink
    {
        [Serializable] sealed class Content { public string type = string.Empty; public string text = string.Empty; }
        [Serializable] sealed class Result { public Content[] content = Array.Empty<Content>(); public bool isError; }

        // the text of the tool's answer; throws with the device's message when the tool failed
        public static async Task<string> CallAsync(string tool, string argumentsJson, CancellationToken ct)
        {
            string json;
            try { json = await MatrixPluginEditor.Instance.CallPlayerToolAsync(tool, argumentsJson, ct); }
            catch (Exception e) when (e.Message.IndexOf("CallPlayerTool", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException("this Matrix server cannot reach devices; reinstall it (Matrix Bridge > Server > Reinstall Binaries).");
            }
            var result = JsonUtility.FromJson<Result>(json);
            if (result == null)
                throw new InvalidOperationException("no answer from the server");
            var text = result.content.Length > 0 ? result.content[0].text : string.Empty;
            if (result.isError)
                throw new InvalidOperationException(text);
            return text;
        }
    }
}
