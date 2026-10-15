#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Feeder.MCP.Editor.HyperTesting;
using Feeder.ToolHost;
using Feeder.ToolHost.Skills;
using NUnit.Framework;
using UnityEditor;

namespace Feeder.MCP.Editor.Tests
{
    // Every skill folder Matrix writes needs its own name: a guide skill that shares the folder of a tool
    // skill is overwritten or deleted when that tool is switched on or off.
    public sealed class SkillIdCollisionTests
    {
        static IEnumerable<(string Folder, string Owner)> SkillFolders()
        {
            foreach (var method in TypeCache.GetMethodsWithAttribute<AiToolAttribute>())
            {
                var tool = method.GetCustomAttribute<AiToolAttribute>()!;
                yield return (SkillFileGenerator.ToolSkillPrefix + tool.Name, $"tool {tool.Name} ({method.DeclaringType?.Name})");
            }

            foreach (var type in TypeCache.GetTypesWithAttribute<AiSkillTypeAttribute>())
            {
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var member in members)
                {
                    var skill = member.GetCustomAttribute<AiSkillAttribute>();
                    if (skill != null)
                        yield return (skill.Name, $"guide skill {type.Name}.{member.Name}");
                }
            }

            yield return (HyperTestingPaths.SkillId, "Hyper Testing guide");
        }

        [Test]
        public void Every_skill_folder_has_one_owner()
        {
            var clashes = SkillFolders()
                .GroupBy(entry => entry.Folder)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key}: {string.Join(", ", group.Select(entry => entry.Owner))}")
                .ToArray();

            Assert.IsEmpty(clashes, "Skill folders written by more than one owner:\n" + string.Join("\n", clashes));
        }

        [Test]
        public void Finds_the_tool_and_guide_skills()
        {
            var folders = SkillFolders().Select(entry => entry.Folder).ToArray();
            Assert.Contains("matrix-script-execute", folders);
            Assert.Contains("matrix-shared-editor", folders);
            Assert.Contains(HyperTestingPaths.SkillId, folders);
        }
    }
}
