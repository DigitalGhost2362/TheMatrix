#nullable enable
using System;
using Feeder.ToolHost.Common.Utils;
using Feeder.Reflect;
using Feeder.MCP.Utils;
using Feeder.Logging;
using UnityEngine;

namespace Feeder.MCP.Runtime.Utils
{
    public class TestSerialization : MonoBehaviour
    {
        [SerializeField] UnityEngine.Object? target;
        [SerializeField] bool recursive = false;

        [Header("Automatic trigger")]
        [SerializeField] bool onAwake = false;
        [SerializeField] bool onEnable = false;
        [SerializeField] bool onStart = false;

        private void Awake()
        {
            if (onAwake)
            {
                SerializeTarget();
            }
        }
        private void OnEnable()
        {
            if (onEnable)
            {
                SerializeTarget();
            }
        }
        private void Start()
        {
            if (onStart)
            {
                SerializeTarget();
            }
        }

        public void SerializeTarget()
        {
            var logger = UnityLoggerFactory.LoggerFactory.CreateLogger(nameof(TestSerialization));
            if (!MatrixPluginRuntime.HasInstance || MatrixPluginRuntime.Instance.ToolHostInstance == null)
                throw new InvalidOperationException("No active MatrixPluginRuntime instance. Call MatrixPluginRuntime.Initialize().Build() first.");
            var reflector = MatrixPluginRuntime.Instance.ToolHostInstance.McpManager.Reflector
                ?? throw new InvalidOperationException("Reflector is null");

            logger.LogInformation($"Serializing target '{target?.name}' of type '{target?.GetType().GetTypeId()}' with recursive={recursive}");

            var serialized = reflector.Serialize(
                obj: target,
                fallbackType: null,
                name: target?.name,
                recursive: recursive,
                context: null,
                logger: logger);

            logger.LogInformation(serialized.ToPrettyJson());
        }
    }
}
