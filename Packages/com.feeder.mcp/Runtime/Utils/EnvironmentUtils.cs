#nullable enable
using System;
using System.Collections.Generic;
using Feeder.ToolHost.Common.Utils;
using Feeder.MCP.Utils;
using Feeder.Logging;
using static Feeder.ToolHost.Common.Consts.MCP.Server;

namespace Feeder.MCP.Runtime.Utils
{
    /// <summary>
    /// Loads MCP plugin configuration from layered sources and (optionally) applies
    /// runtime overrides from environment variables and process command-line arguments.
    ///
    /// Priority (highest wins):
    ///   1. Process command-line flags (any <c>--FEEDER_MATRIX_*</c> variant)
    ///   2. Process environment variables (<c>FEEDER_MATRIX_*</c>)
    ///   3. On-disk config (<c>UserSettings/Feeder-MCP-Config.json</c>)
    ///   4. Built-in defaults
    ///
    /// Overrides applied via env vars / flags are NEVER persisted back to disk —
    /// they are runtime-only. The <see cref="OverrideRecord"/> returned by
    /// <see cref="ApplyEnvironmentOverrides"/> captures both the disk-baseline values
    /// and the override values so that the editor's Save() flow can restore the
    /// baseline before serializing. See <c>MatrixPluginEditor.Save</c>.
    /// </summary>
    public static class EnvironmentUtils
    {
        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger(nameof(EnvironmentUtils));

        // Environment variable names for MCP connection overrides.
        // Only this package's own prefix is read: variables that belong to other tools must never steer this package.
        public const string EnvPrefix = "FEEDER_MATRIX_";
        public const string EnvHost = EnvPrefix + "HOST";
        public const string EnvKeepConnected = EnvPrefix + "KEEP_CONNECTED";
        public const string EnvAuthOption = EnvPrefix + "AUTH_OPTION";
        public const string EnvToken = EnvPrefix + "TOKEN";
        public const string EnvTools = EnvPrefix + "TOOLS";
        public const string EnvStartServer = EnvPrefix + "START_SERVER";
        public const string EnvTransport = EnvPrefix + "TRANSPORT";

        // Field-name keys used by OverrideRecord to track which fields were overridden
        // by env/flags so the Save flow can restore disk-baseline values before writing.
        public const string FieldHost = nameof(MatrixPlugin.UnityConnectionConfig.LocalHost);
        public const string FieldKeepConnected = nameof(MatrixPlugin.UnityConnectionConfig.KeepConnected);
        public const string FieldAuthOption = nameof(MatrixPlugin.UnityConnectionConfig.AuthOption);
        public const string FieldLocalToken = nameof(MatrixPlugin.UnityConnectionConfig.LocalToken);
        public const string FieldTools = nameof(MatrixPlugin.UnityConnectionConfig.EnabledToolsOverride);
        public const string FieldStartServer = nameof(MatrixPlugin.UnityConnectionConfig.KeepServerRunning);
        public const string FieldTransport = nameof(MatrixPlugin.UnityConnectionConfig.TransportMethod);

        /// <summary>
        /// Captures, per overridden field, the disk-baseline value (what was read from JSON)
        /// and the runtime override value applied on top. Allows <c>Save()</c> to round-trip
        /// the baseline to disk while keeping the runtime override on the in-memory config.
        /// Empty when no env/flag overrides were detected.
        /// </summary>
        public sealed class OverrideRecord
        {
            readonly Dictionary<string, object?> _baselineValues = new();
            readonly Dictionary<string, object?> _overrideValues = new();

            public IReadOnlyDictionary<string, object?> BaselineValues => _baselineValues;
            public IReadOnlyDictionary<string, object?> OverrideValues => _overrideValues;

            public bool HasAny => _baselineValues.Count > 0;
            public bool Contains(string fieldName) => _baselineValues.ContainsKey(fieldName);

            internal void Track(string fieldName, object? baselineValue, object? overrideValue)
            {
                _baselineValues[fieldName] = baselineValue;
                _overrideValues[fieldName] = overrideValue;
            }
        }

        /// <summary>
        /// Checks if the current environment is a CI environment.
        /// </summary>
        public static bool IsCi()
        {
            var commandLineArgs = ArgsUtils.ParseCommandLineArguments();

            var ci = commandLineArgs.GetValueOrDefault("CI") ?? Environment.GetEnvironmentVariable("CI");
            var gha = commandLineArgs.GetValueOrDefault("GITHUB_ACTIONS") ?? Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
            var az = commandLineArgs.GetValueOrDefault("TF_BUILD") ?? Environment.GetEnvironmentVariable("TF_BUILD"); // Azure Pipelines

            return string.Equals(ci?.Trim()?.Trim('"'), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(gha?.Trim()?.Trim('"'), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(az?.Trim()?.Trim('"'), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Applies environment-variable and command-line-argument overrides to the given config.
        /// Args (highest priority) override env vars, env vars override the disk-baseline values
        /// already present on <paramref name="config"/>. Returns an <see cref="OverrideRecord"/>
        /// that captures the disk-baseline value for each field that was overridden.
        /// </summary>
        public static OverrideRecord ApplyEnvironmentOverrides(MatrixPlugin.UnityConnectionConfig config)
        {
            var args = ArgsUtils.ParseCommandLineArguments();
            return ApplyEnvironmentOverrides(config, args, Environment.GetEnvironmentVariable);
        }

        /// <summary>
        /// Test-friendly overload. <paramref name="argReader"/> is the parsed command-line
        /// arg dictionary (keys without leading dashes); <paramref name="envReader"/> resolves
        /// environment variables. Either may be empty / always-null to disable that source.
        /// </summary>
        public static OverrideRecord ApplyEnvironmentOverrides(
            MatrixPlugin.UnityConnectionConfig config,
            IReadOnlyDictionary<string, string> argReader,
            Func<string, string?> envReader)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (argReader == null) throw new ArgumentNullException(nameof(argReader));
            if (envReader == null) throw new ArgumentNullException(nameof(envReader));

            var record = new OverrideRecord();

            // Resolve a value with priority: FEEDER_MATRIX_* env-style flag (highest) > env var.
            // Returns null if no source provided a non-empty value.
            string? Resolve(string envKey)
            {
                if (argReader.TryGetValue(envKey, out var envFlagValue)
                    && !string.IsNullOrWhiteSpace(envFlagValue))
                {
                    return envFlagValue;
                }
                var envValue = envReader(envKey);
                if (!string.IsNullOrWhiteSpace(envValue))
                    return envValue;
                return null;
            }

            string Sanitize(string raw) => raw.Trim().Trim('"');

            var rawHost = Resolve(EnvHost);
            if (rawHost != null)
            {
                var host = Sanitize(rawHost).TrimEnd('/');
                if (host.Length > 0)
                {
                    if (!IsLoopbackUrl(host))
                    {
                        _logger.LogWarning("[MCP] Ignored non-loopback host override: {Value}", host);
                    }
                    else if (!string.Equals(host, config.LocalHost, StringComparison.Ordinal))
                    {
                        record.Track(FieldHost, config.LocalHost, host);
                        config.LocalHost = host;
                        _logger.LogInformation("[MCP] Override: {Key}={Value}", FieldHost, host);
                    }
                }
            }

            var rawKeep = Resolve(EnvKeepConnected);
            if (rawKeep != null && bool.TryParse(Sanitize(rawKeep), out var keep) && keep != config.KeepConnected)
            {
                record.Track(FieldKeepConnected, config.KeepConnected, keep);
                config.KeepConnected = keep;
                _logger.LogInformation("[MCP] Override: {Key}={Value}", EnvKeepConnected, keep);
            }

            var rawAuth = Resolve(EnvAuthOption);
            if (rawAuth != null
                && Enum.TryParse<AuthOption>(Sanitize(rawAuth), ignoreCase: true, out var ao)
                && ao != config.AuthOption)
            {
                record.Track(FieldAuthOption, config.AuthOption, ao);
                config.AuthOption = ao;
                _logger.LogInformation("[MCP] Override: {Key}={Value}", EnvAuthOption, ao);
            }

            var rawToken = Resolve(EnvToken);
            if (rawToken != null)
            {
                var token = Sanitize(rawToken);
                if (!string.Equals(token, config.LocalToken, StringComparison.Ordinal))
                {
                    record.Track(FieldLocalToken, config.LocalToken, token);
                    config.LocalToken = token;
                    _logger.LogInformation("[MCP] Override: {Key}=*** (LocalToken)", EnvToken);
                }
            }

            var rawTransport = Resolve(EnvTransport);
            if (rawTransport != null
                && Enum.TryParse<TransportMethod>(Sanitize(rawTransport), ignoreCase: true, out var tm)
                && tm != config.TransportMethod)
            {
                record.Track(FieldTransport, config.TransportMethod, tm);
                config.TransportMethod = tm;
                _logger.LogInformation("[MCP] Override: {Key}={Value}", EnvTransport, tm);
            }

            var rawStart = Resolve(EnvStartServer);
            if (rawStart != null && bool.TryParse(Sanitize(rawStart), out var ss) && ss != config.KeepServerRunning)
            {
                record.Track(FieldStartServer, config.KeepServerRunning, ss);
                config.KeepServerRunning = ss;
                _logger.LogInformation("[MCP] Override: {Key}={Value}", EnvStartServer, ss);
            }

            // EnabledToolsOverride is [JsonIgnore] so it is never persisted regardless of the
            // baseline-restoration logic; we still track it for completeness.
            var rawTools = Resolve(EnvTools);
            if (rawTools != null)
            {
                var ids = rawTools.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var trimmed = new List<string>(ids.Length);
                foreach (var id in ids)
                {
                    var value = Sanitize(id);
                    if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                        trimmed.Add(value);
                }
                record.Track(FieldTools, config.EnabledToolsOverride, trimmed);
                config.EnabledToolsOverride = trimmed;
                _logger.LogInformation("[MCP] Override: {Key}={Value}", EnvTools, rawTools.Trim());
            }

            return record;
        }

        /// <summary>
        /// Applies the BaselineValues from <paramref name="record"/> to <paramref name="config"/>
        /// (i.e. reverses runtime overrides so the in-memory config reflects what is on disk).
        /// Used by the editor's Save() flow to ensure runtime-only overrides are not persisted.
        /// </summary>
        public static void ApplyBaseline(MatrixPlugin.UnityConnectionConfig config, OverrideRecord record)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (record == null) throw new ArgumentNullException(nameof(record));
            ApplyValues(config, record.BaselineValues);
        }

        /// <summary>
        /// Re-applies the OverrideValues from <paramref name="record"/> to <paramref name="config"/>
        /// (i.e. restores runtime overrides after a temporary baseline-restore).
        /// </summary>
        public static void ApplyOverrides(MatrixPlugin.UnityConnectionConfig config, OverrideRecord record)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (record == null) throw new ArgumentNullException(nameof(record));
            ApplyValues(config, record.OverrideValues);
        }

        static void ApplyValues(MatrixPlugin.UnityConnectionConfig config, IReadOnlyDictionary<string, object?> values)
        {
            foreach (var kvp in values)
            {
                switch (kvp.Key)
                {
                    case FieldHost:
                        config.LocalHost = (string?)kvp.Value ?? MatrixPlugin.UnityConnectionConfig.DefaultHost;
                        break;
                    case FieldKeepConnected:
                        if (kvp.Value is bool kc) config.KeepConnected = kc;
                        break;
                    case FieldAuthOption:
                        if (kvp.Value is AuthOption ao) config.AuthOption = ao;
                        break;
                    case FieldLocalToken:
                        config.LocalToken = (string?)kvp.Value;
                        break;
                    case FieldTools:
                        config.EnabledToolsOverride = (List<string>?)kvp.Value;
                        break;
                    case FieldStartServer:
                        if (kvp.Value is bool ss) config.KeepServerRunning = ss;
                        break;
                    case FieldTransport:
                        if (kvp.Value is TransportMethod tm) config.TransportMethod = tm;
                        break;
                    default:
                        // Fail loudly if a new Field* constant is added to Track but forgotten here —
                        // a silent miss would let runtime overrides leak to disk on Save.
                        throw new InvalidOperationException($"Unhandled override field: {kvp.Key}");
                }
            }
        }

        /// <summary>
        /// Returns true if the given URL targets a loopback host
        /// (<c>localhost</c>, <c>127.0.0.0/8</c>, or IPv6 <c>::1</c>).
        /// Used to reject host values that do not point at this machine.
        /// </summary>
        public static bool IsLoopbackUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;
            var host = uri.Host;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;
            if (System.Net.IPAddress.TryParse(host, out var ip))
                return System.Net.IPAddress.IsLoopback(ip);
            return false;
        }
    }
}
