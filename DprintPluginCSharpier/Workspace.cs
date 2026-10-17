using System;
using System.Collections.Generic;
using System.Linq;
using Dprint.Plugins.CSharpier.Configuration;
using Dprint.Plugins.CSharpier.Formatters;

namespace Dprint.Plugins.CSharpier;

public class ResolvedContext
{
    public IReadOnlyList<ConfigurationDiagnostic> Diagnostics { get; init; } = null!;
    public FileFormatter Formatter { get; init; } = null!;
}

public class StoredConfig
{
    public IReadOnlyDictionary<string, object> PluginConfig { get; init; } = null!;
    public GlobalConfiguration GlobalConfig { get; init; } = null!;
    public Lazy<ResolvedContext> DefaultContext { get; init; } = null!;
}

/// <summary>
/// Not thread safe.
/// </summary>
public class Workspace
{
    private readonly Dictionary<uint, StoredConfig> _configs = new();

    public FileFormatter GetFormatter(
        uint configId,
        Dictionary<string, object>? overrideConfig = null
    )
    {
        var config = GetStoredConfig(configId);
        var context =
            overrideConfig == null || overrideConfig.Count == 0
                ? config.DefaultContext.Value
                : CreateResolvedContext(config.GlobalConfig, config.PluginConfig, overrideConfig);
        return context.Formatter;
    }

    public IReadOnlyList<ConfigurationDiagnostic> GetDiagnostics(uint configId)
    {
        return GetStoredConfig(configId).DefaultContext.Value.Diagnostics;
    }

    public void SetConfig(
        uint configId,
        GlobalConfiguration globalConfig,
        Dictionary<string, object> pluginConfig
    )
    {
        _configs[configId] = new StoredConfig
        {
            GlobalConfig = globalConfig,
            PluginConfig = pluginConfig,
            DefaultContext = new Lazy<ResolvedContext>(() =>
                CreateResolvedContext(globalConfig, pluginConfig, new Dictionary<string, object>())
            ),
        };
    }

    public void ReleaseConfig(uint configId)
    {
        _configs.Remove(configId, out var _);
    }

    private StoredConfig GetStoredConfig(uint configId)
    {
        if (_configs.TryGetValue(configId, out var value))
            return value;
        else
            throw new ArgumentOutOfRangeException(
                nameof(configId),
                $"Could not find configuration id: {configId}"
            );
    }

    private static ResolvedContext CreateResolvedContext(
        GlobalConfiguration globalConfig,
        IReadOnlyDictionary<string, object> pluginConfig,
        IReadOnlyDictionary<string, object> overrideConfig
    )
    {
        var config = pluginConfig.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (key, value) in overrideConfig)
        {
            config[key] = value;
        }

        var (resolvedConfig, diagnostics) = ConfigurationResolver.Resolve(globalConfig, config);
        return new ResolvedContext
        {
            Diagnostics = diagnostics,
            Formatter = new FileFormatter(resolvedConfig),
        };
    }
}
