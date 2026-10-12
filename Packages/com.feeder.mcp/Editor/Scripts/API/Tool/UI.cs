#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Feeder.ToolHost;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_UI
    {
        // Sub-pixel layout jitter is common in Yoga/UI Toolkit; anything below this is noise,
        // not a real overflow the user can see.
        internal const float LayoutEpsilon = 1.5f;
        internal const int MaxTreeLines = 3000;
        internal const int MaxIssues = 200;
        internal const int TextPreviewLength = 60;

        internal static EditorWindow FindWindowByName(string windowName)
        {
            if (string.IsNullOrWhiteSpace(windowName))
                throw new ArgumentException("Window name cannot be empty.", nameof(windowName));

            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w != null)
                .ToList();

            var exact = windows.FirstOrDefault(w =>
                string.Equals(w.GetType().Name, windowName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(w.GetType().FullName, windowName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(w.titleContent?.text, windowName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var partialMatches = windows.Where(w =>
                    (w.GetType().FullName ?? string.Empty).IndexOf(windowName, StringComparison.OrdinalIgnoreCase) >= 0
                    || (w.titleContent?.text ?? string.Empty).IndexOf(windowName, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (partialMatches.Count == 1)
                return partialMatches[0];
            if (partialMatches.Count > 1)
                throw new ArgumentException(
                    $"Multiple open windows match '{windowName}': "
                    + string.Join(", ", partialMatches.Select(w => w.GetType().Name).Distinct())
                    + ". Use the exact class name.");

            throw new ArgumentException(
                $"No open EditorWindow matches '{windowName}'. "
                + "Use 'ui-inspect-windows' to list the open windows first.");
        }

        // Resolves '#name' (UQuery by name), '.class' (first match), or a hierarchy index path
        // like '0.2.1'. Empty/null returns the root itself.
        internal static VisualElement ResolveElement(VisualElement root, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return root;

            query = query.Trim();

            if (query.StartsWith("#", StringComparison.Ordinal))
            {
                return root.Q(query.Substring(1))
                    ?? throw new ArgumentException($"No element with name '{query}' found under the window root.");
            }

            if (query.StartsWith(".", StringComparison.Ordinal))
            {
                return root.Q(className: query.Substring(1))
                    ?? throw new ArgumentException($"No element with class '{query}' found under the window root.");
            }

            var parts = query.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            foreach (var part in parts)
            {
                if (!int.TryParse(part, out var index))
                    throw new ArgumentException(
                        $"Invalid element query '{query}'. Use '#name', '.class', or an index path like '0.2.1'.");
                if (index < 0 || index >= current.hierarchy.childCount)
                    throw new ArgumentException(
                        $"Child index {index} is out of range at '[{GetIndexPath(current, root)}]' "
                        + $"(childCount={current.hierarchy.childCount}).");
                current = current.hierarchy[index];
            }
            return current;
        }

        internal static string GetIndexPath(VisualElement element, VisualElement root)
        {
            var parts = new List<string>();
            var current = element;
            while (current != null && current != root)
            {
                var parent = current.hierarchy.parent;
                if (parent == null)
                    break;
                parts.Add(parent.hierarchy.IndexOf(current).ToString());
                current = parent;
            }
            parts.Reverse();
            return parts.Count == 0 ? "root" : string.Join(".", parts);
        }

        internal static string DescribeShort(VisualElement element)
        {
            var sb = new StringBuilder(element.GetType().Name);
            if (!string.IsNullOrEmpty(element.name))
                sb.Append(" #").Append(element.name);
            foreach (var cls in element.GetClasses())
                sb.Append(" .").Append(cls);
            return sb.ToString();
        }

        internal static string FormatRect(Rect rect)
            => $"x={rect.x:0} y={rect.y:0} w={rect.width:0} h={rect.height:0}";

        internal static string TruncateText(string text, int maxLength)
        {
            text = text.Replace("\r", "").Replace("\n", "\\n");
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "…";
        }

        // ScrollView children legitimately overflow their viewport — that is what scrolling is.
        internal static bool IsScrollContainer(VisualElement element)
            => element is ScrollView
               || element.ClassListContains("unity-scroll-view__content-container")
               || element.ClassListContains("unity-scroll-view__content-viewport")
               || element.hierarchy.parent is ScrollView;

        internal static List<string> DetectIssues(VisualElement element)
        {
            var issues = new List<string>();
            if (element.resolvedStyle.display == DisplayStyle.None)
                return issues;

            var wb = element.worldBound;
            if (float.IsNaN(wb.width) || float.IsNaN(wb.height))
                return issues;

            var hasContent = element.hierarchy.childCount > 0
                || (element is TextElement textCheck && !string.IsNullOrEmpty(textCheck.text));
            if ((wb.width < 0.5f || wb.height < 0.5f) && hasContent)
                issues.Add($"ZERO-SIZE (w={wb.width:0.#} h={wb.height:0.#} but has content)");

            if (element is TextElement te && !string.IsNullOrEmpty(te.text)
                && wb.width > 0.5f && wb.height > 0.5f)
            {
                var content = te.contentRect;
                if (!float.IsNaN(content.width) && content.width > 0.5f)
                {
                    var full = te.MeasureTextSize(te.text,
                        0, VisualElement.MeasureMode.Undefined,
                        0, VisualElement.MeasureMode.Undefined);
                    var ellipsis = te.resolvedStyle.textOverflow == TextOverflow.Ellipsis
                        ? ", ellipsis is on" : "";
                    var noWrap = te.resolvedStyle.whiteSpace == WhiteSpace.NoWrap;

                    if (!float.IsNaN(full.x) && !float.IsNaN(full.y))
                    {
                        if (noWrap)
                        {
                            if (full.x > content.width + LayoutEpsilon)
                                issues.Add($"TEXT-CLIPPED-X (needs {full.x:0}px, has {content.width:0}px, nowrap{ellipsis})");
                            if (full.y > content.height + LayoutEpsilon)
                                issues.Add($"TEXT-CLIPPED-Y (needs {full.y:0}px, has {content.height:0}px{ellipsis})");
                        }
                        else
                        {
                            var wrapped = te.MeasureTextSize(te.text,
                                content.width, VisualElement.MeasureMode.Exactly,
                                0, VisualElement.MeasureMode.Undefined);
                            if (!float.IsNaN(wrapped.y) && wrapped.y > content.height + LayoutEpsilon)
                                issues.Add($"TEXT-CLIPPED-Y (wrapped text needs {wrapped.y:0}px height, has {content.height:0}px{ellipsis})");
                        }
                    }
                }
            }

            var parent = element.hierarchy.parent;
            if (parent != null && !IsScrollContainer(parent))
            {
                var pb = parent.worldBound;
                if (!float.IsNaN(pb.width))
                {
                    var overflow = new List<string>();
                    if (wb.xMax > pb.xMax + LayoutEpsilon) overflow.Add($"right +{wb.xMax - pb.xMax:0}px");
                    if (wb.yMax > pb.yMax + LayoutEpsilon) overflow.Add($"bottom +{wb.yMax - pb.yMax:0}px");
                    if (wb.x < pb.x - LayoutEpsilon) overflow.Add($"left -{pb.x - wb.x:0}px");
                    if (wb.y < pb.y - LayoutEpsilon) overflow.Add($"top -{pb.y - wb.y:0}px");
                    if (overflow.Count > 0)
                    {
                        var absolute = element.resolvedStyle.position == Position.Absolute
                            ? ", position:absolute (may be intentional)" : "";
                        // IResolvedStyle does not expose overflow — only the inline style is readable,
                        // so USS-driven clipping cannot be distinguished from spilling here.
                        var parentOverflow = parent.style.overflow;
                        var consequence = parentOverflow.keyword == StyleKeyword.Undefined
                            ? "content is clipped by the parent or spills over siblings"
                            : parentOverflow.value == Overflow.Hidden
                                ? "parent clips → content is CUT OFF"
                                : "spills over sibling elements";
                        issues.Add($"OVERFLOWS-PARENT ({string.Join(", ", overflow)}; {consequence}{absolute})");
                    }
                }
            }

            return issues;
        }

        internal static void AppendTree(StringBuilder sb, VisualElement element, VisualElement root,
            int depth, int maxDepth, bool includeHidden, ref int lines)
        {
            if (lines >= MaxTreeLines)
                return;

            var indent = new string(' ', depth * 2);
            sb.Append(indent).Append('[').Append(GetIndexPath(element, root)).Append("] ")
                .Append(DescribeShort(element))
                .Append("  (").Append(FormatRect(element.worldBound)).Append(')');

            if (element is TextElement te && !string.IsNullOrEmpty(te.text))
                sb.Append("  \"").Append(TruncateText(te.text, TextPreviewLength)).Append('"');

            var display = element.resolvedStyle.display;
            if (display == DisplayStyle.None)
                sb.Append("  [display:none]");
            else if (element.resolvedStyle.visibility == Visibility.Hidden)
                sb.Append("  [hidden]");

            foreach (var issue in DetectIssues(element))
                sb.Append("  !").Append(issue);

            sb.AppendLine();
            lines++;

            if (display == DisplayStyle.None && !includeHidden)
                return;

            var childCount = element.hierarchy.childCount;
            if (depth >= maxDepth)
            {
                if (childCount > 0)
                {
                    sb.Append(indent).Append("  … (").Append(childCount).AppendLine(" children beyond maxDepth)");
                    lines++;
                }
                return;
            }

            for (var i = 0; i < childCount; i++)
                AppendTree(sb, element.hierarchy[i], root, depth + 1, maxDepth, includeHidden, ref lines);
        }

        internal static string BuildElementDetail(EditorWindow window, VisualElement element, VisualElement root)
        {
            var sb = new StringBuilder();
            var rs = element.resolvedStyle;

            sb.Append("Element: ").AppendLine(DescribeShort(element));
            sb.Append("Window: ").Append(window.GetType().FullName)
                .Append(" (\"").Append(window.titleContent?.text).AppendLine("\")");
            sb.Append("IndexPath: ").AppendLine(GetIndexPath(element, root));
            sb.Append("WorldBound: ").AppendLine(FormatRect(element.worldBound));
            sb.Append("ContentRect: ").AppendLine(FormatRect(element.contentRect));

            if (element is TextElement te && !string.IsNullOrEmpty(te.text))
            {
                sb.Append("Text: \"").Append(TruncateText(te.text, 300)).AppendLine("\"");
                var measured = te.MeasureTextSize(te.text,
                    0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined);
                sb.Append("MeasuredTextSize (unconstrained): ")
                    .Append($"w={measured.x:0} h={measured.y:0}").AppendLine();
            }

            var issues = DetectIssues(element);
            sb.Append("Issues: ").AppendLine(issues.Count == 0 ? "none detected" : string.Join(" | ", issues));

            sb.AppendLine("ResolvedStyle:");
            sb.Append("  display=").Append(rs.display)
                .Append(" visibility=").Append(rs.visibility)
                .Append(" opacity=").Append($"{rs.opacity:0.##}")
                .Append(" enabled=").Append(element.enabledInHierarchy).AppendLine();
            sb.Append("  position=").Append(rs.position)
                .Append(" left=").Append($"{rs.left:0.#}").Append(" top=").Append($"{rs.top:0.#}")
                .Append(" right=").Append($"{rs.right:0.#}").Append(" bottom=").Append($"{rs.bottom:0.#}").AppendLine();
            sb.Append("  width=").Append($"{rs.width:0.#}").Append(" height=").Append($"{rs.height:0.#}")
                .Append(" minWidth=").Append(rs.minWidth).Append(" minHeight=").Append(rs.minHeight)
                .Append(" maxWidth=").Append(rs.maxWidth).Append(" maxHeight=").Append(rs.maxHeight).AppendLine();
            sb.Append("  flexDirection=").Append(rs.flexDirection)
                .Append(" flexGrow=").Append($"{rs.flexGrow:0.##}")
                .Append(" flexShrink=").Append($"{rs.flexShrink:0.##}")
                .Append(" flexBasis=").Append(rs.flexBasis)
                .Append(" flexWrap=").Append(rs.flexWrap).AppendLine();
            sb.Append("  alignItems=").Append(rs.alignItems)
                .Append(" alignSelf=").Append(rs.alignSelf)
                .Append(" justifyContent=").Append(rs.justifyContent).AppendLine();
            sb.Append("  margin(t/r/b/l)=").Append($"{rs.marginTop:0.#}/{rs.marginRight:0.#}/{rs.marginBottom:0.#}/{rs.marginLeft:0.#}")
                .Append(" padding(t/r/b/l)=").Append($"{rs.paddingTop:0.#}/{rs.paddingRight:0.#}/{rs.paddingBottom:0.#}/{rs.paddingLeft:0.#}")
                .Append(" border(t/r/b/l)=").Append($"{rs.borderTopWidth:0.#}/{rs.borderRightWidth:0.#}/{rs.borderBottomWidth:0.#}/{rs.borderLeftWidth:0.#}")
                .AppendLine();
            sb.Append("  overflow(inline)=").Append(element.style.overflow)
                .Append(" whiteSpace=").Append(rs.whiteSpace)
                .Append(" textOverflow=").Append(rs.textOverflow)
                .Append(" fontSize=").Append($"{rs.fontSize:0.#}")
                .Append(" unityTextAlign=").Append(rs.unityTextAlign).AppendLine();

            sb.AppendLine("Ancestors (root → element):");
            var chain = new List<VisualElement>();
            var current = element.hierarchy.parent;
            while (current != null)
            {
                chain.Add(current);
                if (current == root)
                    break;
                current = current.hierarchy.parent;
            }
            chain.Reverse();
            foreach (var ancestor in chain)
                sb.Append("  [").Append(GetIndexPath(ancestor, root)).Append("] ")
                    .Append(DescribeShort(ancestor))
                    .Append("  (").Append(FormatRect(ancestor.worldBound)).Append(')').AppendLine();

            var childCount = element.hierarchy.childCount;
            sb.Append("Children (").Append(childCount).AppendLine("):");
            for (var i = 0; i < Math.Min(childCount, 20); i++)
            {
                var child = element.hierarchy[i];
                sb.Append("  [").Append(GetIndexPath(child, root)).Append("] ")
                    .Append(DescribeShort(child))
                    .Append("  (").Append(FormatRect(child.worldBound)).Append(')').AppendLine();
            }
            if (childCount > 20)
                sb.Append("  … ").Append(childCount - 20).AppendLine(" more");

            var sheets = new List<string>();
            current = element;
            while (current != null)
            {
                for (var i = 0; i < current.styleSheets.count; i++)
                {
                    var sheet = current.styleSheets[i];
                    if (sheet != null)
                        sheets.Add($"{sheet.name} (attached on [{GetIndexPath(current, root)}] {current.GetType().Name})");
                }
                if (current == root)
                    break;
                current = current.hierarchy.parent;
            }
            sb.AppendLine("StyleSheets in scope:");
            if (sheets.Count == 0)
                sb.AppendLine("  none found up the hierarchy");
            else
                foreach (var sheet in sheets.Distinct())
                    sb.Append("  ").AppendLine(sheet);

            sb.AppendLine("Hint: element name (#) and classes (.) above map to the UXML/USS source files — search them by these identifiers.");
            return sb.ToString();
        }
    }
}
