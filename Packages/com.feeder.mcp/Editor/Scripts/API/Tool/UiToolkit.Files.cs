#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Xml;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using UnityEditor;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_UiToolkit
    {
        public const string UiToolkitWriteToolId = "uitoolkit-write";

        internal const string UxmlTemplate =
            "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:uie=\"UnityEditor.UIElements\" editor-extension-mode=\"False\">\n" +
            "    <ui:VisualElement name=\"root\" />\n" +
            "</ui:UXML>\n";

        internal const string UssTemplate = "/* USS */\n";

        [AiTool
        (
            UiToolkitWriteToolId,
            Title = "UI Toolkit / Write File",
            Enabled = false
        )]
        [AiSkillDescription("Create or edit a UI Toolkit .uxml or .uss file under Assets/: create, overwrite, append, or replace one piece of text. " +
            "UXML must be well-formed XML and USS must have balanced braces, otherwise nothing is written. Imports the asset and returns its size.")]
        [Description("Writes a UXML/USS file. mode: 'create' (fails if it exists; empty content gives a template), 'overwrite', 'append' (adds content at the end), " +
            "'replace' (replaces the single occurrence of oldText with content). The file is validated before writing (XML parse for UXML, brace balance for USS). " +
            "File writes are not part of the Editor undo stack. Editor-changing.")]
        public string UiToolkitWrite
        (
            [Description("Project path starting with 'Assets/' and ending with '.uxml' or '.uss'.")]
            string path,
            [Description("File text (create/overwrite), text to add (append) or the new text (replace).")]
            string? content = null,
            [Description("'create', 'overwrite', 'append' or 'replace'. Default: 'create'")]
            string mode = "create",
            [Description("Text to replace; must occur exactly once. Only for mode 'replace'.")]
            string? oldText = null
        )
        {
            var p = RequireAssetPath(path, ".uxml", ".uss");
            var kind = (mode ?? string.Empty).Trim().ToLowerInvariant();
            if (kind != "create" && kind != "overwrite" && kind != "append" && kind != "replace")
                throw new ArgumentException("mode must be 'create', 'overwrite', 'append' or 'replace'.");

            return MainThread.Instance.Run(() =>
            {
                var isUxml = p.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase);
                var exists = File.Exists(p);
                string text;
                switch (kind)
                {
                    case "create":
                        if (exists)
                            throw new InvalidOperationException($"'{p}' already exists; use mode 'overwrite', 'append' or 'replace'.");
                        text = string.IsNullOrEmpty(content) ? (isUxml ? UxmlTemplate : UssTemplate) : content!;
                        break;
                    case "overwrite":
                        text = content ?? throw new ArgumentException("content is required for 'overwrite'.");
                        break;
                    default:
                        if (!exists)
                            throw new InvalidOperationException($"'{p}' does not exist.");
                        var current = File.ReadAllText(p);
                        if (kind == "append")
                            text = current + (current.EndsWith("\n") || current.Length == 0 ? string.Empty : "\n") + (content ?? string.Empty);
                        else
                            text = Replace(current, oldText, content);
                        break;
                }

                var problem = Validate(text, isUxml);
                if (problem != null)
                    throw new ArgumentException($"Not written: {problem}");

                var dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(p, text, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);

                var loaded = isUxml
                    ? (UnityEngine.Object?)AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(p)
                    : AssetDatabase.LoadAssetAtPath<StyleSheet>(p);
                return $"{(exists ? "updated" : "created")} {p} bytes={Encoding.UTF8.GetByteCount(text)} imported={(loaded != null)}" +
                    (loaded == null ? " (the importer rejected the file; check the console)" : string.Empty);
            });
        }

        internal static string Replace(string current, string? oldText, string? replacement)
        {
            if (string.IsNullOrEmpty(oldText))
                throw new ArgumentException("oldText is required for 'replace'.");
            var first = current.IndexOf(oldText, StringComparison.Ordinal);
            if (first < 0)
                throw new ArgumentException("oldText was not found in the file.");
            if (current.IndexOf(oldText!, first + oldText!.Length, StringComparison.Ordinal) >= 0)
                throw new ArgumentException("oldText occurs more than once; include more context so it is unique.");
            return current.Substring(0, first) + (replacement ?? string.Empty) + current.Substring(first + oldText.Length);
        }

        // null when the text looks valid
        internal static string? Validate(string text, bool isUxml)
        {
            if (isUxml)
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(text);
                    if (doc.DocumentElement?.LocalName != "UXML")
                        return "the root element must be <ui:UXML>.";
                }
                catch (XmlException ex)
                {
                    return $"UXML is not well-formed XML (line {ex.LineNumber}, position {ex.LinePosition}): {ex.Message}";
                }
                return null;
            }

            var depth = 0;
            var inComment = false;
            for (var i = 0; i < text.Length; i++)
            {
                if (!inComment && text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*') { inComment = true; i++; }
                else if (inComment && text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/') { inComment = false; i++; }
                else if (!inComment && text[i] == '{') depth++;
                else if (!inComment && text[i] == '}' && --depth < 0)
                    return "USS has a '}' without a matching '{'.";
            }
            return depth != 0 ? "USS has an unclosed '{'." : inComment ? "USS has an unclosed comment." : null;
        }
    }
}
