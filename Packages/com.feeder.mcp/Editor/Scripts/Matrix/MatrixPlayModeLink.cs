#nullable enable
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Player;
using UnityEditor;

namespace Feeder.MCP.Editor.Matrix
{
    /// <summary>
    /// Optionally links Play mode to the Matrix server as a player, so the runtime tools can be tried without making a build.
    /// The Editor's own link stays up; the player is a second link beside it.
    /// </summary>
    [InitializeOnLoad]
    internal static class MatrixPlayModeLink
    {
        const string PrefKey = "Feeder.Matrix.PlayModeLink";
        const string MenuPath = Feeder.MCP.Editor.UI.MenuItems.Root + "Server/Link Play Mode As Player";

        static MatrixPlayModeLink() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath, priority = 1010)]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            UnityEditor.Menu.SetChecked(MenuPath, Enabled);
            return MatrixActivation.IsEnabled;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && Enabled && MatrixActivation.IsEnabled)
                MatrixPlayer.Connect("127.0.0.1", MatrixPluginEditor.Port, MatrixPluginEditor.Token ?? string.Empty,
                    MatrixPlayer.ProjectIdFor(MatrixPluginEditor.ProjectRootPath));
            else if (change == PlayModeStateChange.ExitingPlayMode)
                MatrixPlayer.Disconnect();
        }
    }
}
