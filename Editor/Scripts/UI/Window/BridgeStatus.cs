#nullable enable
using System;

namespace Feeder.MCP.Editor.UI
{
    public enum BridgePhase
    {
        Offline,
        Linking,
        Online,
        Working,
        PlayTesting,
        Fault,
    }

    public static class BridgeStatus
    {
        public static BridgePhase Resolve(bool reconnectPending, bool connected, bool connecting, McpServerStatus server, string? fault, bool agentPlay = false, bool working = false)
        {
            if (!string.IsNullOrEmpty(fault))
                return BridgePhase.Fault;
            var linking = reconnectPending || connecting || server == McpServerStatus.Installing || server == McpServerStatus.Starting;
            if (agentPlay && (connected || linking))
                return BridgePhase.PlayTesting;
            if (connected)
                return working ? BridgePhase.Working : BridgePhase.Online;
            if (linking)
                return BridgePhase.Linking;
            return BridgePhase.Offline;
        }

        public static string Word(BridgePhase phase) => phase switch
        {
            BridgePhase.Offline => "OFFLINE",
            BridgePhase.Linking => "LINKING",
            BridgePhase.Online => "ONLINE",
            BridgePhase.Working => "WORKING",
            BridgePhase.PlayTesting => "PLAY TESTING",
            BridgePhase.Fault => "FAULT",
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
        };

        public static float RainEnergy(BridgePhase phase) => phase switch
        {
            BridgePhase.Offline => 0.08f,
            BridgePhase.Linking => 0.5f,
            BridgePhase.Online => 0.75f,
            BridgePhase.Working => 1f,
            BridgePhase.PlayTesting => 1f,
            BridgePhase.Fault => 0.2f,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
        };
    }
}
