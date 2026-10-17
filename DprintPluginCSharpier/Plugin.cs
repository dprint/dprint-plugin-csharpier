using System;
using System.IO;
using System.Threading;
using Dprint.Plugins.CSharpier.Serialization;

namespace Dprint.Plugins.CSharpier;

/// <summary>
/// Handles the requests made by dprint.
/// </summary>
public class Plugin
{
    /// <summary>
    /// Extensions of the files CSharpier formats.
    /// </summary>
    public static readonly string[] FileExtensions =
    [
        "cs",
        "csx",
        "csproj",
        "props",
        "targets",
        "slnx",
        "config",
        "xml",
        "xaml",
        "axaml",
    ];

    private readonly Workspace _workspace = new();

    public byte[] GetPluginInfo()
    {
        return PluginJson.SerializePluginInfo(GetAssemblyVersion());
    }

    public string GetLicenseText()
    {
        using var stream =
            typeof(Plugin).Assembly.GetManifestResourceStream("Dprint.Plugins.CSharpier.LICENSE")
            ?? throw new Exception("Could not find license text.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void RegisterConfig(uint configId, byte[] configJson)
    {
        var (globalConfig, pluginConfig) = PluginJson.DeserializeRegisterConfig(configJson);
        _workspace.SetConfig(configId, globalConfig, pluginConfig);
    }

    public void ReleaseConfig(uint configId)
    {
        _workspace.ReleaseConfig(configId);
    }

    public byte[] GetConfigDiagnostics(uint configId)
    {
        return PluginJson.SerializeDiagnostics(_workspace.GetDiagnostics(configId));
    }

    public byte[] GetResolvedConfig(uint configId)
    {
        return PluginJson.SerializeConfig(_workspace.GetFormatter(configId).Config.ToDictionary());
    }

    public byte[] GetFileMatchingInfo()
    {
        return PluginJson.SerializeFileMatchingInfo(FileExtensions);
    }

    public byte[] CheckConfigUpdates()
    {
        return """{"kind":"ok","data":[]}"""u8.ToArray();
    }

    /// <summary>
    /// Formats the file returning the formatted bytes or null when there was no change.
    /// </summary>
    public byte[]? Format(
        uint configId,
        string filePath,
        byte[] fileBytes,
        (int Start, int End)? byteRange,
        byte[]? overrideConfigJson
    )
    {
        var overrideConfig = overrideConfigJson is null
            ? null
            : PluginJson.DeserializeConfig(overrideConfigJson);
        var formatter = _workspace.GetFormatter(configId, overrideConfig);
        return formatter.Format(filePath, fileBytes, byteRange, CancellationToken.None);
    }

    private static string GetAssemblyVersion()
    {
        var version =
            typeof(Plugin).Assembly.GetName().Version
            ?? throw new Exception("Could not find assembly version.");
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
