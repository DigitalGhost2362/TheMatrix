using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Feeder.Matrix.Health;

/// <summary>
/// Lists the visible windows of a process and the text inside them (dialogs, progress bars,
/// "waiting for ..." popups) without sending them messages, so it never blocks on a hung editor.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowProbe
{
    const int MaxWindows = 20;
    const int MaxChildTexts = 12;
    const int MaxTextChars = 200;

    // child windows named after a code type ("Some.Namespace.Panel") carry no message for a human
    static readonly System.Text.RegularExpressions.Regex TypeName = new(@"^[A-Za-z_]\w*(\.[A-Za-z_]\w*)+$");

    public static IReadOnlyList<string> ReadWindows(int processId)
    {
        var lines = new List<string>();
        EnumWindows((window, _) =>
        {
            if (lines.Count >= MaxWindows)
                return false;
            GetWindowThreadProcessId(window, out var owner);
            if (owner != processId || !IsWindowVisible(window))
                return true;

            var title = Text(window);
            var children = new List<string>();
            EnumChildWindows(window, (child, _) =>
            {
                if (children.Count >= MaxChildTexts)
                    return false;
                var text = Text(child);
                if (text.Length > 0 && !TypeName.IsMatch(text) && IsWindowVisible(child) && !children.Contains(text))
                    children.Add(text);
                return true;
            }, IntPtr.Zero);

            if (title.Length == 0 && children.Count == 0)
                return true;
            var line = new StringBuilder(title.Length == 0 ? "(untitled)" : $"\"{title}\"");
            if (IsHungAppWindow(window))
                line.Append(" [not responding]");
            if (children.Count > 0)
                line.Append(": ").Append(string.Join(" / ", children.Select(c => $"\"{c}\"")));
            lines.Add(line.ToString());
            return true;
        }, IntPtr.Zero);
        return lines;
    }

    /// <summary>True when the OS flags any top-level window of the process as hung (no message pumping for 5 s).</summary>
    public static bool AnyHung(int processId)
    {
        var hung = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == processId && IsWindowVisible(window) && IsHungAppWindow(window))
                hung = true;
            return !hung;
        }, IntPtr.Zero);
        return hung;
    }

    static string Text(IntPtr window)
    {
        var buffer = new char[MaxTextChars + 1];
        var length = InternalGetWindowText(window, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length).Replace("\r", " ").Replace("\n", " ").Trim();
    }

    delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsHungAppWindow(IntPtr window);

    // reads the text the window manager keeps, never sends WM_GETTEXT, so a hung owner cannot block it
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int InternalGetWindowText(IntPtr window, char[] text, int maxCount);
}
