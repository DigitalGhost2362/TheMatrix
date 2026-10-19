#nullable enable
#if !UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost;
using Feeder.Reflect;
using Feeder.MCP.Data;
using Feeder.MCP.Utils;
using Feeder.Logging;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_GameObject
    {
        public static class Error
        {
            public static string NotFoundComponent(int componentInstanceID, IEnumerable<UnityEngine.Component> allComponents)
            {
                var reflector = MatrixPluginEditor.Instance.Reflector ?? throw new Exception("Reflector is not available.");
                var availableComponentsPreview = allComponents
                    .Select((c, i) => reflector.Serialize(
                        c,
                        name: $"[{i}]",
                        recursive: false,
                        logger: UnityLoggerFactory.LoggerFactory.CreateLogger<Tool_GameObject>()
                    ))
                    .ToList();
                var previewJson = availableComponentsPreview.ToJson(reflector);

                var instanceIdSample = new { componentData = availableComponentsPreview[0] }.ToJson(reflector);
                var helpMessage = $"Use 'name=[index]' to specify the component. Or use 'instanceID' to specify the component.\n{instanceIdSample}";

                return $"No component with instanceID '{componentInstanceID}' found in GameObject.\n{helpMessage}\nAvailable components preview:\n{previewJson}";
            }
            public static string NotFoundComponents(ComponentRefList componentRefs, IEnumerable<UnityEngine.Component> allComponents)
            {
                var reflector = MatrixPluginEditor.Instance.Reflector ?? throw new Exception("Reflector is not available.");
                var componentInstanceIDsString = string.Join(", ", componentRefs.Select(cr => cr.ToString()));
                var availableComponentsPreview = allComponents
                    .Select((c, i) => reflector.Serialize(
                        obj: c,
                        fallbackType: typeof(UnityEngine.Component),
                        name: $"[{i}]",
                        recursive: false,
                        logger: UnityLoggerFactory.LoggerFactory.CreateLogger<Tool_GameObject>()
                    ))
                    .ToList();
                var previewJson = availableComponentsPreview.ToJson(reflector);

                return $"No components with instanceIDs [{componentInstanceIDsString}] found in GameObject.\nAvailable components preview:\n{previewJson}";
            }
            public static string InvalidInstanceID(Type holderType, string fieldName)
                => $"Invalid instanceID '{fieldName}' for '{holderType.GetTypeId()}'. It should be a valid field name.";
        }
    }
}
#endif
