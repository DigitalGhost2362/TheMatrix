#nullable enable
using Feeder.ToolHost;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Assets_Prefab
    {
        public static class Error
        {
            public static string PrefabPathIsEmpty()
                => "Prefab path is empty. Pass a path such as 'Assets/Prefabs/Hero.prefab'.";

            public static string NotFoundPrefabAtPath(string path)
                => $"Prefab '{path}' not found. Find prefabs with rql-query: assets(\"t:Prefab\").";

            public static string PrefabPathIsInvalid(string path)
                => $"Prefab path '{path}' is invalid.";

            public static string PrefabStageIsNotOpened()
                => "Prefab stage is not opened. Use 'assets-prefab-open' to open it.";

            public static string PrefabStageIsAlreadyOpened()
                => "Prefab stage is already opened. Use 'assets-prefab-close' to close it.";

            public static string PrefabSaveFailed(string path)
                => $"Saving prefab '{path}' failed (missing script, read-only or locked file?). See the Console for the reason; the prefab stage keeps its unsaved changes.";
        }
    }
}
