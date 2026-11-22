#nullable enable
#if UNITY_EDITOR
using System.IO;
using System.Threading.Tasks;
using Feeder.MCP.Editor.Utils;
using UnityEditor;
using UnityEngine;
using static Feeder.ToolHost.Common.Consts.MCP.Server;

namespace Feeder.MCP.Editor.UI
{
    public static class MenuItems
    {
        public const string Root = "Tools/Feeder/Matrix Bridge/";

        [MenuItem(Root + "Getting Started", priority = -21)]
        public static void RunMatrixSetup() => MatrixSetup.Run();

        [MenuItem(Root + "Skills", priority = -19)]
        public static void ShowSkills() => MatrixSkillsWindow.ShowWindow();

        [MenuItem(Root + "Bridge", priority = -18)]
        public static void ShowWindow() => MatrixBridgeWindow.ShowWindow();

        [MenuItem(Root + "Hyper Testing (Experimental)", priority = -16)]
        public static void ShowHyperTesting() => HyperTesting.HyperTestingWindow.ShowWindow();

        [MenuItem(Root + "VFX Studio (Experimental)", priority = -15)]
        public static void ShowVfxStudio() => Vfx.VfxStudioWindow.ShowWindow();

        [MenuItem(Root + "Skills", true)]
        [MenuItem(Root + "Bridge", true)]
        [MenuItem(Root + "Space (experimental)", true)]
        [MenuItem(Root + "Hyper Testing (Experimental)", true)]
        [MenuItem(Root + "Server/Reinstall Binaries", true)]
        [MenuItem(Root + "Server/Delete Binaries", true)]
        [MenuItem(Root + "Server/Open Logs", true)]
        [MenuItem(Root + "Server/Launch MCP Inspector", true)]
        [MenuItem(Root + "Server/Reset Config", true)]
        private static bool IsSetUp() => MatrixActivation.IsEnabled;

        [MenuItem(Root + "Server/Reinstall Binaries", priority = 1000)]
        public static Task ReinstallServer() => McpServerManager.InstallServerBinaryIfNeeded(force: true);

        [MenuItem(Root + "Server/Delete Binaries", priority = 1001)]
        public static void DeleteServer()
        {
            var result = McpServerManager.DeleteBinaryFolderIfExists();
            if (result)
            {
                NotificationPopupWindow.Show(
                    windowTitle: "Success",
                    title: "MCP Server Binaries Deleted",
                    message: "The MCP server binaries were successfully deleted. You can reinstall them from the Tools menu.",
                    width: 350,
                    minWidth: 350,
                    height: 200,
                    minHeight: 200);
            }
            else
            {
                NotificationPopupWindow.Show(
                    windowTitle: "Error",
                    title: "MCP Server Binaries Not Found",
                    message: "No MCP server binaries were found to delete. They may have already been deleted or were never installed.",
                    width: 350,
                    minWidth: 350,
                    height: 200,
                    minHeight: 200);
            }
        }

        [MenuItem(Root + "Server/Open Logs", priority = 1002)]
        public static void OpenServerLogs()
        {
            var folder = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "FeederMatrix", "logs");
            if (!Directory.Exists(folder))
            {
                Debug.LogWarning($"Folder not found: {folder}");
                return;
            }
            EditorUtility.RevealInFinder(folder);
        }

        [MenuItem(Root + "Server/Launch MCP Inspector", priority = 1004)]
        public static void LaunchMcpInspector()
        {
            if (MatrixPluginEditor.TransportMethod != TransportMethod.streamableHttp)
            {
                NotificationPopupWindow.Show(
                    windowTitle: "Error",
                    title: "HTTP Transport required",
                    message: "The MCP Inspector can only be launched when the transport method is set to HTTP. Please change the transport method in the plugin settings and try again.",
                    width: 350,
                    minWidth: 350,
                    height: 200,
                    minHeight: 200);
                return;
            }

            // Run command in a terminal window: npx @modelcontextprotocol/inspector http://localhost:20000 --transport http
            var npxArgs = $"-y @modelcontextprotocol/inspector {MatrixPluginEditor.Host} --transport http";
            Debug.Log($"Launching MCP Inspector with command: npx {npxArgs}");

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "npx",
                Arguments = npxArgs,
                UseShellExecute = true,
                CreateNoWindow = false,
            };

            try
            {
                System.Diagnostics.Process.Start(processInfo);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                var command = $"{processInfo.FileName} {processInfo.Arguments}";
                NotificationPopupWindow.Show(
                    windowTitle: "Launch Failed",
                    title: "Unable to start MCP Inspector",
                    message:
                        "The MCP Inspector could not be started from Unity.\n\n" +
                        "This usually means that Node.js (and npx) is not installed, or 'npx' is not available on your PATH.\n\n" +
                        "Prerequisites:\n" +
                        " - Install Node.js (which includes npx)\n" +
                        " - Ensure 'npx' is available from your terminal/command prompt\n\n" +
                        "You can try running the following command manually in a terminal:\n" +
                        command + "\n\n" +
                        "System error:\n" +
                        ex.Message,
                    width: 450,
                    minWidth: 450,
                    height: 460,
                    minHeight: 460);
            }
            catch (System.Exception ex)
            {
                var command = $"{processInfo.FileName} {processInfo.Arguments}";
                NotificationPopupWindow.Show(
                    windowTitle: "Launch Failed",
                    title: "Unexpected error starting MCP Inspector",
                    message:
                        "An unexpected error occurred while trying to start the MCP Inspector.\n\n" +
                        "You can try running the following command manually in a terminal:\n" +
                        command + "\n\n" +
                        "Error details:\n" +
                        ex.Message,
                    width: 450,
                    minWidth: 450,
                    height: 460,
                    minHeight: 460);
            }
        }

        [MenuItem(Root + "Server/Reset Config", priority = 2020)]
        public static void ResetConfig()
        {
            MatrixPluginEditor.ResetConfig();
            // Reload Domain to ensure all changes are picked up.
            EditorUtility.RequestScriptReload();
        }
    }
}
#endif