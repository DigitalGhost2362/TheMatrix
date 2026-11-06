#nullable enable
using Feeder.MCP.Editor.UI;
using UnityEditor;

namespace Feeder.MCP.Editor
{
    public static class MatrixTools
    {
        const string Title = "Matrix Tools";

        public static void EnableAll() => Run(
            all: true,
            "Enable every Matrix tool and write a skill for each?\n\n" +
            "Their descriptions are sent with every request, which costs many more tokens than the core set. " +
            "Use Core only in the Skills window to go back.");

        public static void CoreOnly() => Run(
            all: false,
            "Keep only the core Matrix tools enabled and drop the skills of the others?\n\n" +
            "Switch more on later in Tools > Feeder > Matrix Bridge > Skills, or with the tool-set-enabled-state tool.");

        static void Run(bool all, string confirm)
        {
            if (!IsReady() || !EditorUtility.DisplayDialog(Title, confirm, "Apply", "Cancel"))
                return;

            var tools = MatrixSetup.ApplyToolSet(all);
            Show(tools + "\n" + SkillsLine(BridgeAgents.RegenerateSkills()) +
                 "\n\nRestart or reconnect your AI client so it lists the new tool set.");
        }

        static bool IsReady()
        {
            if (MatrixActivation.IsEnabled)
                return true;

            EditorUtility.DisplayDialog(Title, "Matrix is off for this project. Run Tools > Feeder > Matrix Bridge > Setup first.", "OK");
            return false;
        }

        static string SkillsLine(int clients) => clients == 0
            ? "Skills: no wired client generates skills - wire one in Matrix Bridge."
            : $"Skills: regenerated for {clients} client(s).";

        static void Show(string message) => NotificationPopupWindow.Show(
            windowTitle: Title,
            title: "Done",
            message: message,
            width: 480,
            minWidth: 480,
            height: 240,
            minHeight: 240);
    }
}
