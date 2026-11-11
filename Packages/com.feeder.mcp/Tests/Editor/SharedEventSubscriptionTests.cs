using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Feeder.MCP.Editor.Tests
{
    // Unity stops a callback chain at the first handler that throws: a direct subscription both breaks the packages
    // after this one and is lost when one before it throws. See "Shared Unity events" in AGENTS.md.
    public class SharedEventSubscriptionTests
    {
        static readonly string[] ScannedFolders = { "Editor", "Runtime" };

        static string PackageRoot => PackageInfo.FindForAssembly(typeof(SharedEventSubscriptionTests).Assembly).resolvedPath;

        static string[] Offenders(string pattern, params string[] allowedFiles)
        {
            var regex = new Regex(pattern);
            return ScannedFolders
                .Select(folder => Path.Combine(PackageRoot, folder))
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
                .Where(path => !allowedFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => regex.IsMatch(File.ReadAllText(path)))
                .Select(path => path.Substring(PackageRoot.Length).TrimStart('\\', '/'))
                .ToArray();
        }

        [Test]
        public void PlayModeEvents_AreSubscribedThroughEditorEventsOnly()
        {
            var offenders = Offenders(@"playModeStateChanged\s*\+=", "EditorEvents.cs");

            Assert.IsEmpty(offenders, "use EditorEvents.OnPlayModeChanged in: " + string.Join(", ", offenders));
        }

        [Test]
        public void AfterAssemblyReload_IsSubscribedThroughEditorEventsOnly()
        {
            var offenders = Offenders(@"afterAssemblyReload\s*\+=", "EditorEvents.cs");

            Assert.IsEmpty(offenders, "use EditorEvents.OnAfterAssemblyReload in: " + string.Join(", ", offenders));
        }

        [Test]
        public void DelayCall_IsNotUsedForWorkThatMustRun()
        {
            // the capture menu waits two delayCalls for a repaint; losing one only loses that capture
            var offenders = Offenders(@"delayCall\s*\+=", "Screenshot.EditorWindow.cs");

            Assert.IsEmpty(offenders, "use EditorEvents.NextTick in: " + string.Join(", ", offenders));
        }
    }
}
