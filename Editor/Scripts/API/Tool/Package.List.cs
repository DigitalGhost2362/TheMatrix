#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Feeder.ToolHost.Common.Model;
using UnityEditor.PackageManager;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Package
    {
        [Description("Filter for package source when listing installed packages.")]
        public enum PackageSourceFilter
        {
            [Description("Show all packages regardless of source.")]
            All,

            [Description("Packages from the Unity Registry (official Unity packages).")]
            Registry,

            [Description("Packages embedded directly in the project's Packages folder.")]
            Embedded,

            [Description("Packages referenced from a local folder path.")]
            Local,

            [Description("Packages installed from a Git repository URL.")]
            Git,

            [Description("Built-in Unity packages that come with the editor.")]
            BuiltIn,

            [Description("Packages installed from a local tarball (.tgz) file.")]
            LocalTarball
        }

        static ResponseCallTool FormatInstalled(IEnumerable<PackageInfo> packages, PackageSourceFilter sourceFilter, string? nameFilter)
        {
            if (sourceFilter != PackageSourceFilter.All)
            {
                var unitySource = sourceFilter switch
                {
                    PackageSourceFilter.Registry => PackageSource.Registry,
                    PackageSourceFilter.Embedded => PackageSource.Embedded,
                    PackageSourceFilter.Local => PackageSource.Local,
                    PackageSourceFilter.Git => PackageSource.Git,
                    PackageSourceFilter.BuiltIn => PackageSource.BuiltIn,
                    PackageSourceFilter.LocalTarball => PackageSource.LocalTarball,
                    _ => PackageSource.Unknown
                };
                if (unitySource != PackageSource.Unknown)
                    packages = packages.Where(p => p.source == unitySource);
            }

            var ordered = string.IsNullOrEmpty(nameFilter)
                ? packages.OrderBy(p => p.name).ToList()
                : packages
                    .Select(p => (pkg: p, priority: GetSearchPriority(p.name, p.displayName, p.description, nameFilter!)))
                    .Where(x => x.priority > 0)
                    .OrderBy(x => x.priority)
                    .ThenBy(x => x.pkg.name)
                    .Select(x => x.pkg)
                    .ToList();

            return ResponseCallTool.Success(ordered.Count == 0
                ? "no installed packages match"
                : string.Join("\n", ordered.Select(p => $"{p.name} {p.version} {p.source} | {p.displayName ?? p.name} | {TruncateDescription(p.description ?? string.Empty, 120)}")));
        }
    }
}
