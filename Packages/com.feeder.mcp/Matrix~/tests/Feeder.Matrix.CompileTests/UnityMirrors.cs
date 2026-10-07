using Feeder.Matrix.Protocol;

namespace Feeder.Matrix.CompileTests;

// shaped like the editor's json mirrors (Editor/Scripts/Matrix/FeederMatrixMessages.cs): mutable classes, pascal case, no attributes
internal sealed class RegisterRequestMirror
{
    public string ProtocolVersion { get; set; } = FmpConstants.ProtocolVersion;
    public string ProjectId { get; set; } = string.Empty;
    public string UnityInstanceId { get; set; } = string.Empty;
    public string ProjectPathHash { get; set; } = string.Empty;
    public string UnityVersion { get; set; } = "2022.3.62f3";
    public string PluginVersion { get; set; } = "0.0.0";
    public string[] SupportedProtocolVersions { get; set; } = [FmpConstants.ProtocolVersion];
    public string ToolRegistryHash { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public int EditorState { get; set; }
    public string[] PendingOperations { get; set; } = [];
}

internal sealed class RegisterResponseMirror
{
    public string SessionId { get; set; } = string.Empty;
    public string ChosenProtocolVersion { get; set; } = string.Empty;
    public string[] Capabilities { get; set; } = [];
}

internal sealed class CompileRequestMirror
{
    public string Source { get; set; } = string.Empty;
    public string[] ReferencePaths { get; set; } = [];
    public string AssemblyName { get; set; } = "DynamicAssembly";
    public bool SyntaxOnly { get; set; }
    public int TimeoutMs { get; set; }
}

internal sealed class CompileResponseMirror
{
    public bool Success { get; set; }
    public byte[]? Assembly { get; set; }
    public string[] Diagnostics { get; set; } = [];
    public string? Error { get; set; }
}
