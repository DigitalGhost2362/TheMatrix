#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Feeder.MCP.Editor.UI;
using Feeder.MCP.Editor.Utils;
using Feeder.ToolHost.Skills;
using NUnit.Framework;

namespace Feeder.MCP.Editor.Tests
{
    public sealed class SkillCatalogTests
    {
        static (string Group, string Label) Classify(string? title, string name = "some-tool")
        {
            var segments = SkillCatalog.Segments(title, name);
            var group = SkillCatalog.GroupOf(segments, new Dictionary<string, SkillGroup>(), out var drop);
            return (group.Name, SkillCatalog.LabelOf(segments, drop));
        }

        [TestCase("GameObject / Find", "GameObjects", "Find")]
        [TestCase("GameObject / Component / Modify", "GameObjects", "Component / Modify")]
        [TestCase("Object / Get Data", "GameObjects", "Object / Get Data")]
        [TestCase("Scene / Open", "Scenes", "Open")]
        [TestCase("Assets / Find", "Assets", "Find")]
        [TestCase("Assets / Shader / Get Data", "Assets", "Shader / Get Data")]
        [TestCase("Assets / Prefab / Open", "Prefabs", "Open")]
        [TestCase("Script / Read", "Code & Tests", "Script / Read")]
        [TestCase("Method C# / Call", "Code & Tests", "Method C# / Call")]
        [TestCase("Screenshot / Game View", "Vision", "Screenshot / Game View")]
        [TestCase("UI / Inspect Tree", "Vision", "UI / Inspect Tree")]
        [TestCase("Editor / Application / Get State", "Editor", "Application / Get State")]
        [TestCase("Console / Get Logs", "Editor", "Console / Get Logs")]
        [TestCase("Package Manager / Add", "Packages", "Package Manager / Add")]
        [TestCase("Profiler / Capture Frame", "Profiler", "Capture Frame")]
        [TestCase("Tool / List", "Matrix", "Tool / List")]
        [TestCase("Skill (Tool) / Create", "Matrix", "Skill (Tool) / Create")]
        [TestCase("Ping", "Matrix", "Ping")]
        public void Classify_KnownTitle_LandsInItsGroupWithATrimmedLabel(string title, string group, string label)
        {
            Assert.AreEqual((group, label), Classify(title));
        }

        [Test]
        public void Classify_UnknownNamespace_BecomesItsOwnGroup()
        {
            var custom = new Dictionary<string, SkillGroup>();
            var segments = SkillCatalog.Segments("Sample / Get", "sample-get");
            var group = SkillCatalog.GroupOf(segments, custom, out var drop);

            Assert.AreEqual("Sample", group.Name);
            Assert.AreEqual("SA", group.Mono);
            Assert.AreEqual("Get", SkillCatalog.LabelOf(segments, drop));
            Assert.AreSame(group, SkillCatalog.GroupOf(SkillCatalog.Segments("Sample / Other", "sample-other"), custom, out _));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Standalone")]
        public void Classify_NoNamespace_GoesToOther(string? title)
        {
            Assert.AreEqual("Other", Classify(title, "my-tool").Group);
        }

        [Test]
        public void Segments_TrimsAndSkipsEmptyParts()
        {
            CollectionAssert.AreEqual(new[] { "A", "B" }, SkillCatalog.Segments(" A /  / B ", "x"));
            CollectionAssert.AreEqual(new[] { "x" }, SkillCatalog.Segments(" / ", "x"));
        }

        [TestCase(true, null, "ReadOnly")]
        [TestCase(true, false, "ReadOnly")]
        [TestCase(null, null, "Writes")]
        [TestCase(false, null, "Writes")]
        [TestCase(null, true, "Destructive")]
        [TestCase(true, true, "Destructive")]
        public void RiskOf_FollowsTheToolHints(bool? readOnly, bool? destructive, string expected)
        {
            Assert.AreEqual(expected, SkillCatalog.RiskOf(readOnly, destructive).ToString());
        }

        [Test]
        public void Summary_KeepsTheFirstSentenceWithoutBackticks()
        {
            var text = "Find a `GameObject` in the scene. Optionally include data.\nSecond paragraph.";
            Assert.AreEqual("Find a GameObject in the scene.", SkillCatalog.Summary(text));
        }

        [Test]
        public void Summary_DropsParentheticalAsides()
        {
            var text = "List every scene opened (name, path, build flags) as a snapshot (shallow). Use it first.";
            Assert.AreEqual("List every scene opened as a snapshot.", SkillCatalog.Summary(text));
        }

        [Test]
        public void Summary_CutsAtTheFirstDashClause_WhenTheGistIsLongEnough()
        {
            Assert.AreEqual("Get detailed information about a Component.", SkillCatalog.Summary("Get detailed information about a Component \u2014 type, enabled state and fields."));
            Assert.AreEqual("Short \u2014 but kept whole.", SkillCatalog.Summary("Short \u2014 but kept whole."));
        }

        [Test]
        public void Summary_WithoutAPeriod_KeepsTheWholeText()
        {
            Assert.AreEqual("No period here", SkillCatalog.Summary("No period here"));
        }

        [Test]
        public void Summary_KeepsDotsInsideWords()
        {
            Assert.AreEqual("Read a .cs script file.", SkillCatalog.Summary("Read a .cs script file. More."));
        }

        [Test]
        public void Summary_LongSentence_EndsWithAnEllipsis()
        {
            var summary = SkillCatalog.Summary(new string('a', 400));
            Assert.That(summary.Length, Is.LessThanOrEqualTo(150));
            Assert.IsTrue(summary.EndsWith("..."));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Summary_Empty_IsEmpty(string? text)
        {
            Assert.AreEqual(string.Empty, SkillCatalog.Summary(text));
        }

        [Test]
        public void Companions_ReturnsKnownToolsOnce_WithoutSelf()
        {
            var names = new HashSet<string>(new[] { "gameobject-find", "assets-find", "self-tool" });
            var text = "Use 'gameobject-find' first, then 'assets-find'. Not 'unknown-tool' or 'self-tool'. Again 'gameobject-find'.";

            CollectionAssert.AreEqual(new[] { "gameobject-find", "assets-find" }, SkillCatalog.Companions(text, names, "self-tool"));
        }

        [Test]
        public void Companions_IsCappedAtFour()
        {
            var names = new HashSet<string>(Enumerable.Range(1, 8).Select(i => "tool-" + i));
            var text = string.Join(" ", names.Select(name => $"'{name}'"));

            Assert.AreEqual(4, SkillCatalog.Companions(text, names, "self").Count);
        }

        [TestCase(0, "Lean")]
        [TestCase(SkillCatalog.LeanBelow - 1, "Lean")]
        [TestCase(SkillCatalog.LeanBelow, "Moderate")]
        [TestCase(SkillCatalog.HeavyFrom - 1, "Moderate")]
        [TestCase(SkillCatalog.HeavyFrom, "Heavy")]
        public void CostOf_UsesTheThresholds(int tokens, string expected)
        {
            Assert.AreEqual(expected, SkillCatalog.CostOf(tokens).ToString());
        }

        [TestCase(0, "0")]
        [TestCase(410, "410")]
        [TestCase(999, "999")]
        [TestCase(1000, "1k")]
        [TestCase(1318, "1.3k")]
        [TestCase(9010, "9k")]
        [TestCase(12345, "12k")]
        [TestCase(48500, "49k")]
        public void FormatTokens_IsShortAndInvariant(int tokens, string expected)
        {
            Assert.AreEqual(expected, SkillCatalog.FormatTokens(tokens));
        }

        [Test]
        public void Build_OrdersGroupsThenRisk_FlagsCore_AndKeepsOnlyRegisteredCompanions()
        {
            var tools = new[]
            {
                new ToolFacts("scene-open", "Scene / Open", "Open a scene. Use 'assets-find' to locate it."),
                new ToolFacts("assets-delete", "Assets / Delete", "Delete assets. Verify with 'assets-find' and 'ghost-tool' first.", destructiveHint: true),
                new ToolFacts("assets-find", "Assets / Find", "Search assets.", readOnlyHint: true, tokens: 700),
                new ToolFacts("sample-get", "Sample / Get", "A custom skill.", readOnlyHint: true),
                new ToolFacts("scene-list-opened", "Scene / List Opened", "List scenes.", readOnlyHint: true),
            };

            var infos = SkillCatalog.Build(tools, new[] { "assets-find" });

            CollectionAssert.AreEqual(
                new[] { "scene-list-opened", "scene-open", "assets-find", "assets-delete", "sample-get" },
                infos.Select(info => info.Name).ToArray());
            Assert.IsTrue(infos.Single(info => info.Name == "assets-find").IsCore);
            Assert.IsFalse(infos.Single(info => info.Name == "assets-delete").IsCore);
            Assert.AreEqual(700, infos.Single(info => info.Name == "assets-find").Tokens);
            CollectionAssert.AreEqual(new[] { "assets-find" }, infos.Single(info => info.Name == "assets-delete").Companions.ToArray());
            CollectionAssert.AreEqual(new[] { "Scenes", "Assets", "Sample" }, SkillCatalog.GroupsOf(infos).Select(group => group.Name).ToArray());
        }

        [Test]
        public void Build_WithoutATitleOrText_FallsBackToTheNameAndAnEmptySummary()
        {
            var infos = SkillCatalog.Build(new[] { new ToolFacts("bare-tool", null, null) }, Array.Empty<string>());

            var info = infos.Single();
            Assert.AreEqual("Other", info.Group.Name);
            Assert.AreEqual("bare-tool", info.Label);
            Assert.AreEqual(string.Empty, info.Summary);
        }
    }

    // tool skills go to matrix-<tool>; a SKILL.md this package did not write is never changed or deleted
    public sealed class SkillFileOwnershipTests
    {
        const string OwnBody = "Call this tool through the MCP client connected to the local Matrix Bridge server.\n\nRead the /feeder-mcp-initial-setup skill for local connection setup.";
        const string FirstVersionBody = "Call this tool through the MCP client connected to the local Matrix AI Connector server.\n\nRead the /feeder-mcp-initial-setup skill for local connection setup.";
        const string ForeignBody = "Written by another tool.\nRead the /other-initial-setup skill to set it up.";

        string _skills = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _skills = Path.Combine(Path.GetTempPath(), "matrix-skills-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_skills);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_skills))
                Directory.Delete(_skills, true);
        }

        string Skill(string folder, string body)
        {
            var path = Path.Combine(_skills, folder);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "SKILL.md"), $"---\nname: {folder}\ndescription: x\n---\n\n{body}\n");
            return path;
        }

        [Test]
        public void RemoveOrphanedToolSkills_DeletesOnlyOurSkillsOfToolsThatAreGone()
        {
            Assume.That(MatrixPluginEditor.HasInstance && MatrixPluginEditor.Instance.Tools != null, "Matrix Bridge is not active, so no tool manager is built.");
            var tools = MatrixPluginEditor.Instance.Tools!.GetAllTools().ToList();
            var live = Skill(SkillFileGenerator.ToolSkillPrefix + tools[0].Name, OwnBody);
            var gone = Skill(SkillFileGenerator.ToolSkillPrefix + "tool-that-was-removed", OwnBody);
            var foreign = Skill(SkillFileGenerator.ToolSkillPrefix + "someone-elses", ForeignBody);
            var handWritten = Skill(SkillFileGenerator.ToolSkillPrefix + "shared-editor", "A skill body with no tool footer.");

            new SkillFileGenerator().RemoveOrphanedToolSkills(tools, _skills);

            Assert.IsTrue(Directory.Exists(live));
            Assert.IsFalse(Directory.Exists(gone));
            Assert.IsTrue(Directory.Exists(foreign));
            Assert.IsTrue(Directory.Exists(handWritten));
        }

        [Test]
        public void UnityGenerator_ToolSkillCarriesTheOwnMark()
        {
            var generator = Activator.CreateInstance(typeof(UnitySkillFileGenerator));
            var method = typeof(UnitySkillFileGenerator).GetMethod("BuildToolCommand", BindingFlags.Instance | BindingFlags.NonPublic);
            var text = new StringBuilder();
            method!.Invoke(generator, new object?[] { text, null, "http://localhost:1", "{}" });
            StringAssert.Contains(SkillFileGenerator.OwnSkillMark, text.ToString());
        }

        [Test]
        public void IsOwnSkillFile_EveryVersionOfOurToolSkill_True()
        {
            Assert.IsTrue(SkillFileGenerator.IsOwnSkillFile(Path.Combine(Skill("a", OwnBody), "SKILL.md")));
            Assert.IsTrue(SkillFileGenerator.IsOwnSkillFile(Path.Combine(Skill("b", FirstVersionBody), "SKILL.md")));
        }

        [Test]
        public void IsOwnSkillFile_ForeignOrMissing_False()
        {
            Assert.IsFalse(SkillFileGenerator.IsOwnSkillFile(Path.Combine(Skill("assets-find", ForeignBody), "SKILL.md")));
            Assert.IsFalse(SkillFileGenerator.IsOwnSkillFile(Path.Combine(_skills, "missing", "SKILL.md")));
        }

        [Test]
        public void DeleteOwnSkill_OurOldUnprefixedSkill_RemovesFolder()
        {
            var folder = Skill("assets-find", OwnBody);

            Assert.IsTrue(SkillFileGenerator.DeleteOwnSkill(folder));
            Assert.IsFalse(Directory.Exists(folder));
        }

        [Test]
        public void DeleteOwnSkill_AnotherToolsSkill_LeftByteForByte()
        {
            var folder = Skill("assets-find", ForeignBody);
            var before = File.ReadAllText(Path.Combine(folder, "SKILL.md"));

            Assert.IsFalse(SkillFileGenerator.DeleteOwnSkill(folder));
            Assert.AreEqual(before, File.ReadAllText(Path.Combine(folder, "SKILL.md")));
        }

        [Test]
        public void DeleteOwnSkill_PrefixedSkillWithExtraFile_KeepsThatFileAndFolder()
        {
            var folder = Skill(SkillFileGenerator.ToolSkillPrefix + "assets-find", OwnBody);
            File.WriteAllText(Path.Combine(folder, "notes.md"), "hand written");

            Assert.IsTrue(SkillFileGenerator.DeleteOwnSkill(folder));
            CollectionAssert.AreEqual(new[] { Path.Combine(folder, "notes.md") }, Directory.GetFileSystemEntries(folder));
        }

        [Test]
        public void DeleteOwnSkill_NoFolder_False()
        {
            Assert.IsFalse(SkillFileGenerator.DeleteOwnSkill(Path.Combine(_skills, "missing")));
        }
    }
}
