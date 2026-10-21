#nullable enable
using Feeder.MCP.Editor.UI;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Utils;
using UnityEditor;
using UnityEngine;
using ILogger = Feeder.Logging.ILogger;

namespace Feeder.MCP.Editor
{
    [InitializeOnLoad]
    public static partial class Startup
    {
        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger(nameof(Startup));

        static bool _activated;

        static Startup()
        {
            // an asset import worker loads every editor assembly; the bridge must never start inside one
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            if (MatrixActivation.IsEnabled)
                Activate();
        }

        internal static void Activate()
        {
            MatrixPluginEditor.Instance.BuildToolHostIfNeeded();
            MatrixPluginEditor.Instance.AddUnityLogCollectorIfNeeded(() => new BufferedFileLogStorage());

            if (_activated)
                return;
            _activated = true;

            if (Application.dataPath.Contains(" "))
                Debug.LogError("The project path contains spaces, which may cause issues during usage of Matrix Bridge. Please consider the move the project to a folder without spaces.");

            SubscribeOnEditorEvents();

            // Initialize sub-systems
            API.Tool_Tests.Init();
            PackageUtils.Init();

            BridgeAgents.RegenerateSkillsIfChanged();

            // Write MCP config files for auto-configure-enabled agents (once per session).
            EditorApplication.delayCall += AutoConfigureAgents;
        }
    }
}
