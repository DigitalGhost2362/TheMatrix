#nullable enable
using Feeder.MCP.Editor.UI;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Utils;
using Feeder.Logging;
using UnityEditor;
using UnityEngine.UIElements;
using ILogger = Feeder.Logging.ILogger;

namespace Feeder.MCP.Editor.MatrixSpace
{
    /// <summary>
    /// A colored frame with a tag laid over an editor window: one retained UI Toolkit element that costs
    /// nothing per frame, added or removed only when the state it shows changes.
    /// </summary>
    internal static class ViewFrame
    {
        static readonly string[] _uxmlPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uxml/PlayTestFrame.uxml");
        static readonly string[] _ussPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uss/PlayTestFrame.uss");
        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger(nameof(ViewFrame));

        static VisualTreeAsset? _tree;
        static StyleSheet? _sheet;

        /// <summary>Puts the frame on the view, or only updates its tag when it is already there.</summary>
        public static void Show(EditorWindow view, string name, string variant, string tag)
        {
            var root = view.rootVisualElement;
            var frame = root.Q(name);
            if (frame == null)
            {
                frame = Create(name, variant);
                if (frame == null)
                    return;
                root.Add(frame);
            }
            var label = frame.Q<Label>("ptf-tag");
            if (label != null && label.text != tag)
                label.text = tag;
        }

        public static void Hide(EditorWindow view, string name) => view.rootVisualElement.Q(name)?.RemoveFromHierarchy();

        static VisualElement? Create(string name, string variant)
        {
            if (_tree == null)
                _tree = EditorAssetLoader.LoadAssetAtPath<VisualTreeAsset>(_uxmlPaths, _logger);
            if (_sheet == null)
                _sheet = EditorAssetLoader.LoadAssetAtPath<StyleSheet>(_ussPaths, _logger);
            if (_tree == null || _sheet == null)
            {
                _logger.LogError("{method} PlayTestFrame.uxml or PlayTestFrame.uss could not be loaded.", nameof(Create));
                return null;
            }

            var frame = _tree.Instantiate();
            frame.name = name;
            frame.pickingMode = PickingMode.Ignore;
            frame.styleSheets.Add(_sheet);
            frame.AddToClassList("ptf-host");
            frame.AddToClassList("ptf-host--" + variant);
            BridgeFont.Apply(frame);
            return frame;
        }
    }
}
