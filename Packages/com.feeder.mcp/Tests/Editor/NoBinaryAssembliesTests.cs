using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Feeder.MCP.Editor.Tests
{
    public class NoBinaryAssembliesTests
    {
        static readonly string[] BinaryExtensions = { ".dll", ".so", ".dylib", ".pdb", ".exe", ".nupkg" };
        static readonly string[] AllowedPrecompiledReferences = { "nunit.framework.dll" };

        [Serializable]
        class AsmdefProbe
        {
            public string name;
            public string[] precompiledReferences;
        }

        static string PackageRoot => PackageInfo.FindForAssembly(typeof(NoBinaryAssembliesTests).Assembly).resolvedPath;

        static string Relative(string path) => path.Substring(PackageRoot.Length).TrimStart('\\', '/');

        static IEnumerable<string> PackageFiles() =>
            Directory.EnumerateFiles(PackageRoot, "*", SearchOption.AllDirectories)
                .Where(path => !Relative(path).Split('\\', '/').Any(segment => segment.EndsWith("~", StringComparison.Ordinal)));

        [Test]
        public void PackageShipsNoBinaryAssemblies()
        {
            var binaries = PackageFiles()
                .Where(path => BinaryExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .Select(Relative)
                .ToArray();

            Assert.IsEmpty(binaries, "binary files inside the package: " + string.Join(", ", binaries));
        }

        [Test]
        public void NoAsmdefReferencesPrecompiledAssemblies()
        {
            var offenders = PackageFiles()
                .Where(path => path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                .Select(path => JsonUtility.FromJson<AsmdefProbe>(File.ReadAllText(path)))
                .Where(asmdef => asmdef.precompiledReferences != null)
                .SelectMany(asmdef => asmdef.precompiledReferences
                    .Where(reference => !AllowedPrecompiledReferences.Contains(reference))
                    .Select(reference => asmdef.name + " -> " + reference))
                .ToArray();

            Assert.IsEmpty(offenders, "precompiled references: " + string.Join(", ", offenders));
        }
    }
}
