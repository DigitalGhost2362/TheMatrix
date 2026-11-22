#nullable enable
using Feeder.MCP.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.Reflect.Utils;
using UnityEditor.PackageManager;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Package
    {

        public const string PackageSearchToolId = "package-search";
        [AiTool
        (
            PackageSearchToolId,
            Title = "Package Manager / Search",
            ReadOnlyHint = true,
            IdempotentHint = true,
            OpenWorldHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Search Unity's package registry plus locally installed packages by query string, or list the installed packages (installedOnly=true). " +
            "Returns available versions and installation status. " +
            "Online mode fetches exact matches from the live registry then supplements with cached substring matches.")]
        [AiSkillBody("Search for packages in both Unity Package Manager registry and installed packages. " +
            "Use this to find packages by name before installing them. " +
            "Results are prioritized: exact name match, exact display name match, name substring, display name substring, description substring. " +
            "Note: Online mode fetches exact matches from live registry, then supplements with cached substring matches.\n\n" +
            "## Inputs\n\n" +
            "- `query` — package id, name, display name, or description keyword (case-insensitive). Required unless `installedOnly`.\n" +
            "- `maxResults` (default 10) — caps the returned list when searching.\n" +
            "- `offlineMode` (default `true`) — when `false`, hits the live registry for exact matches; cached registry " +
            "data still backs the substring matches in both modes.\n" +
            "- `installedOnly` (default `false`) — list the packages installed in the project instead of searching the registry. " +
            "`query` then filters by name / displayName / description (optional), `sourceFilter` restricts by source " +
            "(`All`, `Registry`, `Embedded`, `Local`, `Git`, `BuiltIn`, `LocalTarball`), `directDependenciesOnly` keeps only packages " +
            "listed in `manifest.json`. `maxResults` does not apply.\n\n" +
            "## Result\n\n" +
            "One line per package. Search: name, latest version, installed version or `no`, display name, description, top-5 compatible versions. " +
            "Installed list: name, version, source, display name, description.")]
        [Description("Search packages in the Unity registry and installed packages (query), or list installed packages (installedOnly=true, optional query / sourceFilter / directDependenciesOnly).")]
        public async Task<ResponseCallTool> Search
        (
            [Description("Package id, name or description keyword, e.g. 'com.unity.textmeshpro' or 'rendering' (case-insensitive). Optional when installedOnly: then it filters the installed list.")]
            string? query = null,
            [Description("Maximum number of results to return when searching. Default: 10")]
            int maxResults = 10,
            [Description("Whether to perform the search in offline mode (uses cached registry data only). Default: true. Set to false to fetch latest exact matches from Unity registry.")]
            bool offlineMode = true,
            [Description("List the installed packages instead of searching the registry. Default: false")]
            bool installedOnly = false,
            [Description("installedOnly: filter by package source.")]
            PackageSourceFilter sourceFilter = PackageSourceFilter.All,
            [Description("installedOnly: only direct dependencies (packages in manifest.json). Default: false")]
            bool directDependenciesOnly = false
        )
        {
            if (!installedOnly && string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be empty. Please provide a package name or search term.");

            if (maxResults <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxResults), "Maximum results must be greater than zero.");

            return await MainThread.Instance.RunAsync(async () =>
            {
                var listRequest = Client.List(offlineMode: true, includeIndirectDependencies: !(installedOnly && directDependenciesOnly));
                while (!listRequest.IsCompleted)
                    await Task.Yield();

                if (installedOnly)
                {
                    if (listRequest.Status == StatusCode.Failure)
                        return ResponseCallTool.Error(Error.PackageListFailed(listRequest.Error?.message ?? "Unknown error"));
                    return FormatInstalled(listRequest.Result, sourceFilter, query);
                }

                var text = query!;
                var installedPackages = listRequest.Status == StatusCode.Success
                    ? listRequest.Result.ToList()
                    : new List<PackageInfo>();

                var installedByName = installedPackages.ToDictionary(p => p.name, p => p);

                var results = new List<PackageSearchResult>();
                var addedPackageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Online mode: first fetch exact matches from live registry, then supplement with cached substring matches
                if (!offlineMode)
                {
                    var onlineSearchRequest = Client.Search(text, offlineMode: false);
                    while (!onlineSearchRequest.IsCompleted)
                        await Task.Yield();

                    if (onlineSearchRequest.Status == StatusCode.Success)
                    {
                        var onlineResults = onlineSearchRequest.Result
                            .Select(p => (pkg: p, priority: GetSearchPriority(p.name, p.displayName, p.description, text)))
                            .Where(x => x.priority > 0)
                            .OrderBy(x => x.priority)
                            .Take(maxResults)
                            .Select(x => x.pkg);

                        foreach (var pkg in onlineResults)
                        {
                            results.Add(CreateSearchResult(pkg, installedByName));
                            addedPackageNames.Add(pkg.name);
                        }
                    }
                }

                // Search cached registry data for substring matches (both online and offline modes)
                // This provides substring search that the online API doesn't support
                if (results.Count < maxResults)
                {
                    var cachedSearchRequest = Client.SearchAll(offlineMode: true);
                    while (!cachedSearchRequest.IsCompleted)
                        await Task.Yield();

                    if (cachedSearchRequest.Status == StatusCode.Success)
                    {
                        var cachedResults = cachedSearchRequest.Result
                            .Where(p => !addedPackageNames.Contains(p.name))
                            .Select(p => (pkg: p, priority: GetSearchPriority(p.name, p.displayName, p.description, text)))
                            .Where(x => x.priority > 0)
                            .OrderBy(x => x.priority)
                            .Take(maxResults - results.Count)
                            .Select(x => x.pkg);

                        foreach (var pkg in cachedResults)
                        {
                            results.Add(CreateSearchResult(pkg, installedByName));
                            addedPackageNames.Add(pkg.name);
                        }
                    }
                }

                // Also search through installed packages that match the query
                // This catches packages from Git, local, embedded sources not in registry
                var matchingInstalled = installedPackages
                    .Where(p => !addedPackageNames.Contains(p.name))
                    .Select(p => (pkg: p, priority: GetSearchPriority(p.name, p.displayName, p.description, text)))
                    .Where(x => x.priority > 0)
                    .OrderBy(x => x.priority)
                    .Take(maxResults - results.Count)
                    .Select(x => x.pkg);

                foreach (var pkg in matchingInstalled)
                {
                    var result = new PackageSearchResult
                    {
                        Name = pkg.name,
                        DisplayName = pkg.displayName ?? pkg.name,
                        LatestVersion = pkg.version,
                        Description = TruncateDescription(pkg.description ?? string.Empty, 200),
                        IsInstalled = true,
                        InstalledVersion = pkg.version,
                        AvailableVersions = pkg.versions?.compatible?.Take(5).ToList() ?? new List<string>()
                    };

                    results.Add(result);
                }

                return ResponseCallTool.Success(results.Count == 0
                    ? "no packages found"
                    : string.Join("\n", results.Select(r =>
                        $"{r.Name} latest={r.LatestVersion} installed={r.InstalledVersion ?? "no"} | {r.DisplayName} | {r.Description} | {string.Join(",", r.AvailableVersions)}")));
            }).Unwrap();
        }

        private static PackageSearchResult CreateSearchResult(PackageInfo pkg, Dictionary<string, PackageInfo> installedByName)
        {
            return new PackageSearchResult
            {
                Name = pkg.name,
                DisplayName = pkg.displayName ?? pkg.name,
                LatestVersion = pkg.version,
                Description = TruncateDescription(pkg.description ?? string.Empty, 200),
                IsInstalled = installedByName.ContainsKey(pkg.name),
                InstalledVersion = installedByName.TryGetValue(pkg.name, out var installed)
                    ? installed.version
                    : null,
                AvailableVersions = pkg.versions?.compatible?.Take(5).ToList() ?? new List<string>()
            };
        }

        private static string TruncateDescription(string description, int maxLength)
        {
            if (string.IsNullOrEmpty(description) || description.Length <= maxLength)
                return description;

            return description.Substring(0, maxLength - 3) + "...";
        }
    }
}
