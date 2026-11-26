#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Feeder.MCP.Player;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Device
{
    // the LAN listener a device build links to: off by default, per project, kept in EditorPrefs
    internal static class MatrixDeviceSettings
    {
        // invented default: the loopback port plus 1000; change it in the Matrix Device window
        const int PortOffset = 1000;

        static string Key(string name) => "Feeder.Matrix.Device." + MatrixPlayer.ProjectIdFor(MatrixPluginEditor.ProjectRootPath) + "." + name;

        public static bool LanEnabled => EditorPrefs.GetBool(Key("Lan"), false) && Token.Length > 0;

        public static int LanPort
        {
            get => EditorPrefs.GetInt(Key("Port"), MatrixPluginEditor.Port + PortOffset);
            set => EditorPrefs.SetInt(Key("Port"), value);
        }

        public static string Token => EditorPrefs.GetString(Key("Token"), string.Empty);

        // empty = the first address of an interface with a gateway
        public static string Host
        {
            get
            {
                var chosen = EditorPrefs.GetString(Key("Host"), string.Empty);
                var addresses = Addresses();
                return addresses.Contains(chosen) ? chosen : addresses.FirstOrDefault() ?? string.Empty;
            }
            set => EditorPrefs.SetString(Key("Host"), value);
        }

        // apk output folder, served by the install page
        public static string Folder => Path.Combine(MatrixPluginEditor.ProjectRootPath, "Builds", "MatrixDevice");

        public static string PageUrl => $"http://{Host}:{LanPort}/device?t={Uri.EscapeDataString(Token)}";

        public static string ServerArguments() => LanEnabled
            ? $" --FeederMatrix:LanPort={LanPort} --FeederMatrix:LanToken={Token} --FeederMatrix:DeviceFolder=\"{Folder}\""
            : string.Empty;

        // a new token each time it is turned on, so an old build or an old QR stops working; the server restarts to listen
        public static void SetLan(bool enabled)
        {
            if (enabled)
                EditorPrefs.SetString(Key("Token"), NewToken());
            EditorPrefs.SetBool(Key("Lan"), enabled);
            RestartServer();
        }

        public static void RestartServer()
        {
            McpServerManager.StopServer();
            var deadline = EditorApplication.timeSinceStartup + 20;
            void Wait()
            {
                var stopped = McpServerManager.ServerStatus.CurrentValue == McpServerStatus.Stopped;
                if (!stopped && EditorApplication.timeSinceStartup < deadline)
                    return;
                EditorApplication.update -= Wait;
                McpServerManager.StartServer();
            }
            EditorApplication.update += Wait;
        }

        // what a device build carries to link back to this Editor's server
        public static string BuildConfigJson() => JsonUtility.ToJson(new Feeder.MCP.Device.MatrixDevice.Config
        {
            host = Host,
            port = LanPort,
            token = Token,
            project = MatrixPlayer.ProjectIdFor(MatrixPluginEditor.ProjectRootPath),
        });

        // IPv4 addresses of interfaces that are up, the ones with a default gateway first
        public static List<string> Addresses()
        {
            var result = new List<(string address, bool gateway)>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    var properties = nic.GetIPProperties();
                    var gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));
                    foreach (var unicast in properties.UnicastAddresses)
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(unicast.Address))
                            result.Add((unicast.Address.ToString(), gateway));
                }
            }
            catch (NetworkInformationException e)
            {
                Debug.LogWarning("[Matrix device] network interfaces unreadable: " + e.Message);
            }
            return result.OrderByDescending(r => r.gateway).Select(r => r.address).Distinct().ToList();
        }

        static string NewToken()
        {
            var bytes = new byte[18];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
        }
    }
}
