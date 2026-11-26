#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace Feeder.MCP.Editor.Device
{
    // a build with MATRIX_DEVICE: the link config travels in a Resources file that exists only for the build, and the
    // linker keeps the Matrix and game assemblies whole so the player link and 'call' / 'wait-until' work under IL2CPP
    internal sealed class MatrixDeviceBuild : IDisposable, IUnityLinkerProcessor
    {
        public const string Define = "MATRIX_DEVICE";
        const string TempFolder = "Assets/_MxDeviceBuild";

        static bool _active;

        public int callbackOrder => 0;

        public static IDisposable Prepare(ref BuildPlayerOptions options)
        {
            var resources = TempFolder + "/Resources";
            Directory.CreateDirectory(resources);
            var config = resources + "/" + Feeder.MCP.Device.MatrixDevice.ConfigResource + ".json";
            File.WriteAllText(config, MatrixDeviceSettings.BuildConfigJson());
            AssetDatabase.ImportAsset(config, ImportAssetOptions.ForceSynchronousImport);
            options.extraScriptingDefines = (options.extraScriptingDefines ?? Array.Empty<string>()).Append(Define).Distinct().ToArray();
            _active = true;
            return new MatrixDeviceBuild();
        }

        public void Dispose()
        {
            _active = false;
            AssetDatabase.DeleteAsset(TempFolder);
        }

        public string? GenerateAdditionalLinkXmlFile(BuildReport report, UnityEditor.UnityLinker.UnityLinkerBuildPipelineData data)
        {
            if (!_active)
                return null;
            // CompilationPipeline ignores the build's extra MATRIX_DEVICE define, so it leaves out Feeder.MCP.Device;
            // nothing references that assembly and the linker would drop it whole, link and diagnostics included
            var keep = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(a => a.name.StartsWith("Feeder.", StringComparison.Ordinal) ||
                            a.sourceFiles.Any(f => f.StartsWith("Assets/", StringComparison.Ordinal)))
                .Select(a => a.name)
                .Append(typeof(Feeder.MCP.Device.MatrixDevice).Assembly.GetName().Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal);
            var xml = new StringBuilder("<linker>\n");
            foreach (var name in keep)
                xml.Append("  <assembly fullname=\"").Append(name).Append("\" preserve=\"all\"/>\n");
            xml.Append("</linker>\n");
            var path = Path.GetFullPath(Path.Combine("Temp", "MatrixDevice", "link.xml"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml.ToString());
            return path;
        }

        // still part of the interface on older Unity versions
        public void OnBeforeRun(BuildReport report, UnityEditor.UnityLinker.UnityLinkerBuildPipelineData data) { }
        public void OnAfterRun(BuildReport report, UnityEditor.UnityLinker.UnityLinkerBuildPipelineData data) { }
    }
}
