#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Feeder.MCP.Editor.API.BuildRunner;
using Feeder.MCP.Editor.Device;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Device
    {
        public const string DeviceToolId = "device";

        [AiTool(DeviceToolId, Title = "Editor / Device", Enabled = false)]
        [AiSkillDescription("The phone running a Matrix Device build: whether it is linked, its model and FPS, the install page, and a Unity Profiler capture recorded on the phone and summarized in the Editor.")]
        [AiSkillBody(SkillBody)]
        [Description("Matrix Device phone. action = status | profile. status: LAN, install page, newest APK, last device build, linked phone with model and FPS. " +
                     "profile: records 'seconds' of Unity Profiler on the phone (Development Build), loads it into the Profiler window and answers with frame times and the heaviest main thread markers.")]
        public ResponseCallTool Run
        (
            [Description("status | profile")]
            string action = "status",
            [Description("profile: seconds to record, 1-30. Default 5.")]
            int seconds = 5,
            [RequestID]
            string? requestId = null
        )
        {
            var verb = (action ?? "status").Trim().ToLowerInvariant();
            if (verb != "status" && verb != "profile")
                return ResponseCallTool.Error($"Unknown action '{action}'. Use status or profile.");
            var head = MainThread.Instance.Run(StatusHead);
            Func<CancellationToken, Task<string>> work = verb == "status"
                ? async ct => head + await PhoneAsync(ct)
                : async ct => await DeviceProfile.RunAsync(Math.Max(1, Math.Min(30, seconds)), ct);
            // the phone answers over the network; the result always follows through the request id
            if (string.IsNullOrEmpty(requestId))
                return ResponseCallTool.Error("This call has no request id to answer later; call it through the Matrix server.");
            _ = DeliverAsync(work, requestId!);
            return ResponseCallTool.Processing(verb == "status" ? "Asking the phone." : $"Recording {seconds} s on the phone.").SetRequestID(requestId!);
        }

        static async Task DeliverAsync(Func<CancellationToken, Task<string>> work, string requestId)
        {
            ResponseCallTool response;
            try { response = ResponseCallTool.Success(await work(CancellationToken.None)); }
            catch (Exception e) { response = ResponseCallTool.Error(e.Message); }
            await MatrixPluginEditor.NotifyToolRequestCompleted(new RequestToolCompletedData { RequestId = requestId, Result = response.SetRequestID(requestId) });
        }

        static string StatusHead()
        {
            var text = new StringBuilder();
            text.Append(MatrixDeviceSettings.LanEnabled
                ? $"LAN on: {MatrixDeviceSettings.Host}:{MatrixDeviceSettings.LanPort}. Install page: {MatrixDeviceSettings.PageUrl}\n"
                : "LAN off: Tools > Feeder > Matrix Bridge > Device > LAN on.\n");
            var apk = Directory.Exists(MatrixDeviceSettings.Folder)
                ? new DirectoryInfo(MatrixDeviceSettings.Folder).GetFiles("*.apk").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            text.Append(apk == null ? "APK: none yet (build-start device=true).\n" : $"APK: {apk.FullName} ({apk.Length / 1048576.0:0.0} MB, {apk.LastWriteTime:yyyy-MM-dd HH:mm})\n");
            var job = BuildJobStore.Read();
            if (job != null && job.device)
                text.Append($"Last device build: {job.state}{(job.error.Length > 0 ? " - " + job.error : string.Empty)}\n");
            return text.ToString();
        }

        static async Task<string> PhoneAsync(CancellationToken ct)
        {
            try
            {
                var stats = await DeviceLink.CallAsync("device-profiler", "{\"action\":\"stats\",\"frames\":120}", ct);
                return "Phone: linked.\n" + stats + "\n";
            }
            catch (Exception e)
            {
                return "Phone: not linked (" + e.Message + "). Open the app on the phone; same Wi-Fi as this PC.\n";
            }
        }

        const string SkillBody = @"The phone running a Matrix Device build (scripting define MATRIX_DEVICE).

- `status`: LAN listener, install page URL (its QR is in Tools > Feeder > Matrix Bridge > Device), newest APK, last device build, and when the phone is linked its model, GPU, screen, FPS and counters.
- `profile` + `seconds`: records a Unity Profiler capture on the phone (needs a Development Build), copies it to `Docs/Playtest_Runs/<stamp>-profile-device/profile.raw`, loads it into the Profiler window (replacing what it showed) and answers with frame time percentiles, the heaviest main thread markers and the top GC allocators.

Tools that run on the phone itself while it is linked: `device-logs` (its console, with a cursor), `device-profiler` (action=stats: FPS, frame percentiles, hitches, draw calls, memory), `runtime-scene-info`, `playtest-device` (driven by `playtest device=true`). Call them directly or through `tool-call`.

Build for the phone: `build-start device=true` (development=true for `profile`). Installing is the person's step: they scan the QR in Matrix Device and tap Install.";
    }
}
