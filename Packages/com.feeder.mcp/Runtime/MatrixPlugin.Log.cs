#nullable enable

using System;
using Feeder.Reflect;
using Feeder.MCP.Utils;
using Feeder.Logging;

namespace Feeder.MCP
{
    public partial class MatrixPlugin : IDisposable
    {
        public void LogTrace(string message, Type sourceClass, params object?[] args)
        {
            UnityLoggerFactory.LoggerFactory
                .CreateLogger(sourceClass.GetTypeShortName())
                .LogTrace(message, args);
        }
        public void LogDebug(string message, Type sourceClass, params object?[] args)
        {
            UnityLoggerFactory.LoggerFactory
                .CreateLogger(sourceClass.GetTypeShortName())
                .LogDebug(message, args);
        }
        public void LogInfo(string message, Type sourceClass, params object?[] args)
        {
            UnityLoggerFactory.LoggerFactory
                .CreateLogger(sourceClass.GetTypeShortName())
                .LogInformation(message, args);
        }
        public void LogWarn(string message, Type sourceClass, params object?[] args)
        {
            UnityLoggerFactory.LoggerFactory
                .CreateLogger(sourceClass.GetTypeShortName())
                .LogWarning(message, args);
        }
        public void LogError(string message, Type sourceClass, params object?[] args)
        {
            UnityLoggerFactory.LoggerFactory
                .CreateLogger(sourceClass.GetTypeShortName())
                .LogError(message, args);
        }
        public void LogException(string message, Type sourceClass, params object?[] args)
        {
            UnityLoggerFactory.LoggerFactory
                .CreateLogger(sourceClass.GetTypeShortName())
                .LogCritical(message, args);
        }
    }
}
