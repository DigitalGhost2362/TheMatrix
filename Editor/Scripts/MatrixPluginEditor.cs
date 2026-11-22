#nullable enable
using Feeder.MCP.Runtime.Utils;

namespace Feeder.MCP
{
    /// <summary>
    /// Editor-only singleton that owns the persistent MCP connection used
    /// by Unity Editor tooling (Matrix Bridge window, configurators, etc.).
    /// Lives in the Editor assembly — not accessible from Runtime code.
    /// </summary>
    public partial class MatrixPluginEditor : MatrixPlugin
    {
        /// <summary>
        /// Captures runtime overrides (env vars / CLI flags) that were layered on top of the
        /// on-disk config during construction. Used by <see cref="Save"/> to round-trip the
        /// disk-baseline values to disk while keeping the runtime overrides on the in-memory
        /// config — runtime-only overrides MUST NOT leak into the persisted JSON.
        /// </summary>
        internal EnvironmentUtils.OverrideRecord RuntimeOverrides { get; private set; }
            = new EnvironmentUtils.OverrideRecord();

        protected MatrixPluginEditor() : base()
        {
            var config = GetOrCreateConfig(out var wasCreated);
            unityConnectionConfig = config;
            ApplyLogLevel(unityConnectionConfig.LogLevel);
            // Layered loader: disk values are already on `config`; env vars and CLI flags
            // are applied here as runtime overrides. The OverrideRecord captures the
            // disk-baseline values so Save() can persist them without leaking the overrides.
            RuntimeOverrides = EnvironmentUtils.ApplyEnvironmentOverrides(unityConnectionConfig);
            if (wasCreated)
                Save();
            IncrementSingletonCount();
        }

        public void Validate()
        {
            var changed = false;
            var data = unityConnectionConfig ??= new UnityConnectionConfig();

            if (string.IsNullOrEmpty(data.LocalHost))
            {
                data.LocalHost = UnityConnectionConfig.DefaultHost;
                changed = true;
            }

            // Data was changed during validation, need to notify subscribers
            if (changed)
                NotifyChanged(data);
        }

        public override void Dispose()
        {
            DisposeMatrixAdapter();
            DecrementSingletonCount();
            base.Dispose();
            DisposeToolHostInstance();
        }
    }
}
