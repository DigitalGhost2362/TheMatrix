#nullable enable
using Feeder.MCP.Data;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Utils;
using UnityEditor;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Assets
    {
        // fixed set, not Path.GetInvalidFileNameChars(): that one is OS-dependent and projects move between platforms
        public static readonly char[] InvalidFileNameChars = new[]
        {
            '\\', '<', '>', ':', '"', '|', '?', '*',
            '\0', '\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07',
            '\x08', '\x09', '\x0A', '\x0B', '\x0C', '\x0D', '\x0E', '\x0F',
            '\x10', '\x11', '\x12', '\x13', '\x14', '\x15', '\x16', '\x17',
            '\x18', '\x19', '\x1A', '\x1B', '\x1C', '\x1D', '\x1E', '\x1F'
        };

        public const string AssetsCreateFolderToolId = "assets-create-folder";
        [AiTool
        (
            AssetsCreateFolderToolId,
            Title = "Assets / Create Folder",
            Enabled = false
        )]
        [AiSkillDescription("Create folders under Assets/, including every missing level ('A/B/C'). Returns their GUIDs.")]
        [AiSkillBody("- `inputs` - `{ParentFolderPath, NewFolderName}` entries; `NewFolderName` may be nested (`A/B/C`) and missing parents are created.\n\n" +
            "An existing target folder is reported in `Errors`. Each entry succeeds or fails on its own.")]
        [Description("Create folders under Assets/, with every missing level. Returns the new folders' GUIDs and per-entry errors.")]
        public CreateFolderResponse CreateFolders
        (
            [Description("Folders to create: {ParentFolderPath, NewFolderName}; the name may be nested, e.g. 'A/B'.")]
            CreateFolderInput[] inputs
        )
        {
            if (inputs == null || inputs.Length == 0)
                throw MxGuard.Fail("'inputs' is empty. Pass e.g. [{\"ParentFolderPath\":\"Assets\",\"NewFolderName\":\"Art/Icons\"}].");
            return MainThread.Instance.Run(() =>
            {
                var response = new CreateFolderResponse();
                foreach (var input in inputs)
                {
                    try
                    {
                        var name = MxGuard.NotEmpty(input.NewFolderName, "NewFolderName", "Icons").Trim('/');
                        var bad = name.IndexOfAny(InvalidFileNameChars);
                        if (bad >= 0)
                            throw MxGuard.Fail($"Folder name '{name}' contains the invalid character '{name[bad]}'.");
                        var target = MxGuard.AssetPath((input.ParentFolderPath ?? string.Empty).TrimEnd('/') + "/" + name);
                        if (AssetDatabase.IsValidFolder(target))
                            throw MxGuard.Fail($"'{target}' already exists.");
                        Mx.EnsureFolder(target);
                        (response.CreatedFolderGuids ??= new List<string>()).Add(AssetDatabase.AssetPathToGUID(target));
                    }
                    catch (ToolCallerException e)
                    {
                        (response.Errors ??= new List<string>()).Add(e.Message);
                    }
                }
                if (response.CreatedFolderGuids?.Any() == true)
                    EditorUtils.RepaintAllEditorWindows();
                return response;
            });
        }
    }
}
