#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.ToolHost;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.MCP.Data;
using Feeder.MCP.Runtime.Extensions;
using Feeder.MCP.Utils;
using Feeder.Logging;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Object
    {
        public const string ObjectGetDataToolId = "object-get-data";
        [AiTool
        (
            ObjectGetDataToolId,
            Title = "Object / Get Data",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("Get serialized data for a Unity `UnityEngine.Object` — all serializable fields " +
            "and properties. Supports token-saving path-scoped reads via `paths` or `viewQuery`. " +
            "Pair with '" + ObjectModifyToolId + "' when you need to write back. " +
            "For asset files on disk use '" + Tool_Assets.AssetsGetDataToolId + "' instead.")]
        [AiSkillBody("Get data of the specified Unity Object. " +
            "Returns serialized data of the object including its properties and fields. " +
            "If need to modify the data use '" + ObjectModifyToolId + "' tool.\n\n" +
            "## When to use / when not\n\n" +
            "- Use for any live `UnityEngine.Object` you already have a reference to (by instanceID) — components, " +
            "scene objects, in-memory assets.\n" +
            "- Do NOT use for asset files on disk — use '" + Tool_Assets.AssetsGetDataToolId + "' instead. " +
            "To find a scene GameObject use '" + Tool_GameObject.GameObjectFindToolId + "'.\n\n" +
            "## Path-scoped reads (token-saving)\n\n" +
            "Supply `paths` (a list of paths) to read only the listed fields/elements via `Reflector.TryReadAt`, or " +
            "`viewQuery` (a `ViewQuery`) to navigate to a subtree and/or filter by name regex / max depth / type via " +
            "`Reflector.View`. These two parameters are mutually exclusive — supply at most one. When neither is " +
            "supplied the full object is serialized (backwards compatible).\n\n" +
            "## Path syntax\n\n" +
            "`fieldName`, `nested/field`, `arrayField/[i]`, `dictField/[key]`. Leading `#/` is stripped.")]
        [Description("Get data of the specified Unity Object. " +
            "Returns serialized data of the object including its properties and fields. " +
            "If need to modify the data use '" + ObjectModifyToolId + "' tool.\n\n" +
            "Path-scoped reads (token-saving): supply '" + "paths" + "' (a list of paths) to read only the listed " +
            "fields/elements via Reflector.TryReadAt, or '" + "viewQuery" + "' (a ViewQuery) to navigate to a " +
            "subtree and/or filter by name regex / max depth / type via Reflector.View. " +
            "These two parameters are mutually exclusive — supply at most one. " +
            "When neither is supplied the full object is serialized as before (backwards compatible).\n" +
            "Path syntax: 'fieldName', 'nested/field', 'arrayField/[i]', 'dictField/[key]'. Leading '#/' is stripped.")]
        public SerializedMember? GetData
        (
            ObjectRef objectRef,
            [Description("Optional. List of paths to read individually via Reflector.TryReadAt. " +
                "Each path may target a different depth. " +
                "Path syntax: 'fieldName', 'nested/field', 'arrayField/[i]', 'dictField/[key]'. " +
                "Mutually exclusive with '" + "viewQuery" + "'.")]
            List<string>? paths = null,
            [Description("Optional. View-query filter routed through Reflector.View — combines a starting Path, " +
                "a case-insensitive NamePattern regex, MaxDepth, and an optional TypeFilter. " +
                "Mutually exclusive with '" + "paths" + "'.")]
            ViewQuery? viewQuery = null
        )
        {
            if (objectRef == null)
                throw new ArgumentNullException(nameof(objectRef));

            if (!objectRef.IsValid(out var error))
                throw new ArgumentException(error, nameof(objectRef));

            var hasPaths = paths != null && paths.Count > 0;
            var hasViewQuery = viewQuery != null;
            if (hasPaths && hasViewQuery)
                throw new ArgumentException(
                    $"'{nameof(paths)}' and '{nameof(viewQuery)}' are mutually exclusive — supply at most one.");

            return MainThread.Instance.Run(() =>
            {
                var obj = objectRef.FindObject();
                if (obj == null)
                    throw new Exception("Not found UnityEngine.Object with provided data.");

                var reflector = MatrixPluginEditor.Instance.Reflector ?? throw new Exception("Reflector is not available.");
                var logger = UnityLoggerFactory.LoggerFactory.CreateLogger<Tool_Object>();

                if (hasPaths)
                    return PathReadHelper.BuildPathReadAggregate(reflector, obj, obj.name, paths!, logger);

                if (hasViewQuery)
                    return reflector.View(obj, viewQuery, logs: null, logger: logger)
                        ?? new SerializedMember { name = obj.name, typeName = obj.GetType().FullName ?? string.Empty };

                // Backwards-compatible default: full recursive serialization.
                return reflector.Serialize(
                    obj,
                    name: obj.name,
                    recursive: true,
                    logger: logger
                );
            });
        }
    }
}
