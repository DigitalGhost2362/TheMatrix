#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Feeder.MCP.Editor.UI;
using AiAgentConfiguratorRegistry = Feeder.ToolHost.AgentConfig.AiAgentConfiguratorRegistry;
using static Feeder.ToolHost.Common.Consts.MCP.Server;

namespace Feeder.MCP.Editor.MatrixSpace.Setup
{
    /// <summary>
    /// Shared step factories for per-vendor agent setups. A typical pipeline:
    /// write MCP config → check existing install → ensure prerequisites → install →
    /// locate executable → verify. Login stays manual (the LOGIN button).
    /// </summary>
    public abstract class AgentSetupBase : IAgentSetup
    {
        public abstract string PresetId { get; }
        public abstract IReadOnlyList<SetupStep> BuildSteps();

        // ── Shared steps ─────────────────────────────────────────────────

        /// <summary>
        /// Writes this Unity project's MCP server entry into the agent's own config file via
        /// the shared configurator registry (same call path as <c>Startup.AutoConfigureAgents</c>).
        /// Needs no CLI, so it runs first — a failed install still leaves MCP configured.
        /// </summary>
        protected static SetupStep StepWriteMcpConfig(string configuratorId) => new("Write MCP config", ctx =>
        {
            try
            {
                var configurator = AiAgentConfiguratorRegistry.GetByAgentId(configuratorId);
                if (configurator == null)
                {
                    foreach (var candidate in AiAgentConfiguratorRegistry.All)
                    {
                        if (string.Equals(candidate.AgentId, configuratorId, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(candidate.AgentName, configuratorId, StringComparison.OrdinalIgnoreCase))
                        {
                            configurator = candidate;
                            break;
                        }
                    }
                }

                if (configurator == null)
                {
                    ctx.Log($"No MCP configurator '{configuratorId}' available — skipped.");
                    return true;
                }

                var settings = AgentConfiguratorSettingsFactory.Create();
                var config = MatrixPluginEditor.TransportMethod == TransportMethod.stdio
                    ? configurator.GetStdioConfig(settings)
                    : configurator.GetHttpConfig(settings);
                config.Configure();
                ctx.Log($"MCP config written: {config.ConfigPath}");

                // Keeps the config self-healing on future editor sessions.
                MatrixPluginEditor.SetAutoConfigureAgent(configurator.AgentId, true);
                MatrixPluginEditor.Instance.Save();
            }
            catch (Exception ex)
            {
                // The CLI is still usable standalone; report and continue.
                ctx.Log($"MCP config write failed (non-fatal): {ex.Message}");
            }

            return true;
        }, mainThread: true);

        protected static SetupStep StepCheckInstalled() => new("Check existing install", ctx =>
        {
            string? path = null;
            try { path = AgentBackendCatalog.ResolveExecutablePath(ctx.Preset); }
            catch (Exception) { /* treated as not installed */ }

            if (path != null)
            {
                ctx.SkipInstall = true;
                ctx.ResolvedExecutablePath = path;
                ctx.Log($"Already installed: {path}");
            }
            else
            {
                ctx.Log("CLI not found — installing…");
            }

            return true;
        }, mainThread: true);

        protected static SetupStep StepEnsureNpm() => new("Check Node.js / npm", ctx =>
        {
            if (ctx.SkipInstall)
                return true;

            var npm = FindNpm();
            if (npm == null)
            {
                ctx.Log("npm not found — installing Node.js LTS via winget (this can take a few minutes)…");
                SetupCommandRunner.Run("winget",
                    "install --id OpenJS.NodeJS.LTS -e --accept-source-agreements --accept-package-agreements",
                    ctx.Log);
                npm = FindNpm();
                if (npm == null)
                {
                    ctx.Log($"npm still missing — checked PATH (where.exe npm) and {NodeJsNpmPath}.");
                    ctx.Log("Node.js could not be installed automatically. Opening nodejs.org — install it, then press SETUP again.");
                    ctx.OpenUrl("https://nodejs.org");
                    return false;
                }
            }

            ctx.NpmPath = npm;
            ctx.Log($"npm: {npm}");
            return true;
        });

        protected static SetupStep StepNpmInstall(string package) => new($"npm install -g {package}", ctx =>
        {
            if (ctx.SkipInstall)
                return true;

            ctx.Log($"Installing {package}…");
            var exitCode = SetupCommandRunner.Run(ctx.NpmPath!, $"install -g {package}", ctx.Log);
            if (exitCode != 0)
            {
                ctx.Log($"npm install failed (exit {exitCode}). Opening the vendor install guide.");
                if (!string.IsNullOrEmpty(ctx.Preset.InstallUrl))
                    ctx.OpenUrl(ctx.Preset.InstallUrl);
                return false;
            }

            return true;
        });

        /// <summary>
        /// Finds the freshly installed CLI. The editor's PATH is stale after an install, so
        /// well-known install locations are probed and the absolute path is persisted into the
        /// agent's Executable setting — every later launch works without an editor restart.
        /// </summary>
        protected static SetupStep StepResolveExecutable(string exeName, params string[] extraCandidates)
            => new("Locate executable", ctx =>
        {
            if (ctx.ResolvedExecutablePath != null)
                return true;

            var candidates = new List<string>
            {
                SetupCommandRunner.NpmShimPath(exeName),
                SetupCommandRunner.LocalBinPath(exeName + ".exe"),
                SetupCommandRunner.LocalBinPath(exeName + ".cmd"),
            };
            candidates.AddRange(extraCandidates);

            var path = SetupCommandRunner.Find(exeName, candidates.ToArray());
            if (path == null)
            {
                ctx.Log($"'{exeName}' not found on PATH (where.exe). Probed locations:");
                foreach (var candidate in candidates)
                    ctx.Log($"  - {candidate}");
                ctx.Log($"'{exeName}' still not found after install. Opening the vendor install guide — install manually, then press SETUP again.");
                if (!string.IsNullOrEmpty(ctx.Preset.InstallUrl))
                    ctx.OpenUrl(ctx.Preset.InstallUrl);
                return false;
            }

            ctx.ResolvedExecutablePath = path;
            ctx.RunOnUi(() => StoreExecutablePath(ctx.Preset, path));
            ctx.Log($"Executable: {path}");
            return true;
        });

        protected static SetupStep StepVerify() => new("Verify (--version)", ctx =>
        {
            var path = ctx.ResolvedExecutablePath;
            if (path == null)
                return false;

            // Runs through SetupCommandRunner so stderr and the exit code land in the log —
            // e.g. a broken npm shim prints "'node' is not recognized" here.
            string? version = null;
            var exitCode = SetupCommandRunner.Run(path, "--version", line =>
            {
                ctx.Log("  " + line);
                if (version == null &&
                    !line.StartsWith("Running:", StringComparison.Ordinal) &&
                    !line.StartsWith("Exit code:", StringComparison.Ordinal))
                {
                    version = line;
                }
            }, 60000);

            if (exitCode != 0 || version == null)
            {
                ctx.Log($"Verify failed (exit {exitCode}) — the output above shows the reason.");
                return false;
            }

            ctx.Log($"OK: {version}");
            return true;
        });

        // ── Helpers ──────────────────────────────────────────────────────

        protected static string NodeJsNpmPath => Path.Combine(
            Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files",
            "nodejs", "npm.cmd");

        protected static string? FindNpm() => SetupCommandRunner.Find("npm", NodeJsNpmPath);

        private static void StoreExecutablePath(AgentBackendPreset preset, string path)
        {
            if (preset.IsClaude)
            {
                MatrixSpaceSettings.ClaudeExecutablePath.Value = path;
                ClaudeCliLocator.InvalidateCache();
            }
            else
            {
                var setting = AgentBackendCatalog.ExecutableSetting(preset);
                setting.Value = path;
            }
        }
    }
}
