using System;
using System.Collections.Generic;
using System.Linq;

namespace Dprint.Plugins.CSharpier.Configuration;

/// <summary>
/// Removes the properties from the plugin configuration as they're resolved.
/// </summary>
public class ConfigurationResolutionContext
{
    private readonly Dictionary<string, object> _pluginConfig;
    private readonly List<ConfigurationDiagnostic> _diagnostics = new();

    public ConfigurationResolutionContext(IReadOnlyDictionary<string, object> pluginConfig)
    {
        _pluginConfig = pluginConfig.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public IReadOnlyList<ConfigurationDiagnostic> Diagnostics => _diagnostics;

    public IEnumerable<string> GetConfigKeys()
    {
        return _pluginConfig.Keys.ToList(); // make a copy
    }

    public int? RemoveInt(string propertyName) => RemoveValue<int>(propertyName);

    public bool? RemoveBool(string propertyName) => RemoveValue<bool>(propertyName);

    public string? RemoveString(string propertyName)
    {
        if (!_pluginConfig.Remove(propertyName, out var value))
            return null;
        if (value is string stringValue)
            return stringValue;
        AddDiagnostic(propertyName, $"Value must be a {nameof(String)}");
        return null;
    }

    public void AddDiagnostic(string propertyName, string message)
    {
        _diagnostics.Add(new ConfigurationDiagnostic(propertyName, message));
    }

    private T? RemoveValue<T>(string propertyName)
        where T : struct
    {
        if (!_pluginConfig.Remove(propertyName, out var value))
            return null;
        if (value is T typedValue)
            return typedValue;
        AddDiagnostic(propertyName, $"Value must be a {typeof(T).Name}");
        return null;
    }
}
