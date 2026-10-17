using System.Collections.Generic;
using CSharpier.Core;

namespace Dprint.Plugins.CSharpier.Configuration;

public static class ConfigurationResolver
{
    /// <summary>
    /// Prefix of the properties that only apply to C# files.
    /// </summary>
    public const string CSharpPrefix = "csharp.";

    /// <summary>
    /// Prefix of the properties that only apply to XML files.
    /// </summary>
    public const string XmlPrefix = "xml.";

    public static (
        ResolvedConfiguration Config,
        IReadOnlyList<ConfigurationDiagnostic> Diagnostics
    ) Resolve(GlobalConfiguration globalConfig, IReadOnlyDictionary<string, object> pluginConfig)
    {
        var context = new ConfigurationResolutionContext(pluginConfig);
        // properties without a prefix apply to all languages and override the global configuration
        var lineWidth = RemovePositiveInt(context, "lineWidth") ?? globalConfig.LineWidth;
        var indentWidth = RemovePositiveInt(context, "indentWidth") ?? globalConfig.IndentWidth;
        var useTabs = context.RemoveBool("useTabs") ?? globalConfig.UseTabs;
        var newLineKind =
            RemoveNewLineKind(context, "newLineKind")
            ?? (
                globalConfig.NewLineKind is null
                    ? null
                    : ParseNewLineKind(context, "newLineKind", globalConfig.NewLineKind)
            );

        var config = new ResolvedConfiguration
        {
            CSharp = ResolveLanguage(CSharpPrefix, Formatter.CSharp),
            Xml = ResolveLanguage(XmlPrefix, Formatter.XML),
            IncludeGenerated = context.RemoveBool("includeGenerated") ?? false,
            XmlWhitespaceSensitivity = RemoveXmlWhitespaceSensitivity(
                context,
                "xmlWhitespaceSensitivity"
            ),
        };

        foreach (var key in context.GetConfigKeys())
            context.AddDiagnostic(key, "Unknown configuration property name.");

        return (config, context.Diagnostics);

        LanguageConfiguration ResolveLanguage(string prefix, Formatter formatter)
        {
            // CSharpier's defaults depend on the kind of file
            var defaults = new PrinterOptions(formatter, XmlWhitespaceSensitivity.Strict);
            return new LanguageConfiguration(
                LineWidth: RemovePositiveInt(context, $"{prefix}lineWidth")
                    ?? lineWidth
                    ?? defaults.Width,
                IndentWidth: RemovePositiveInt(context, $"{prefix}indentWidth")
                    ?? indentWidth
                    ?? defaults.IndentSize,
                UseTabs: context.RemoveBool($"{prefix}useTabs") ?? useTabs ?? defaults.UseTabs,
                NewLineKind: RemoveNewLineKind(context, $"{prefix}newLineKind")
                    ?? newLineKind
                    ?? NewLineKind.Auto
            );
        }
    }

    private static int? RemovePositiveInt(
        ConfigurationResolutionContext context,
        string propertyName
    )
    {
        var value = context.RemoveInt(propertyName);
        if (value <= 0)
        {
            context.AddDiagnostic(propertyName, "Value must be greater than 0.");
            return null;
        }
        return value;
    }

    private static NewLineKind? RemoveNewLineKind(
        ConfigurationResolutionContext context,
        string propertyName
    )
    {
        var value = context.RemoveString(propertyName);
        return value is null ? null : ParseNewLineKind(context, propertyName, value);
    }

    private static NewLineKind? ParseNewLineKind(
        ConfigurationResolutionContext context,
        string propertyName,
        string value
    )
    {
        switch (value)
        {
            case "auto":
                return NewLineKind.Auto;
            case "lf":
                return NewLineKind.Lf;
            case "crlf":
                return NewLineKind.Crlf;
            case "system":
                return NewLineKind.System;
            default:
                context.AddDiagnostic(propertyName, $"Unknown new line kind: {value}");
                return null;
        }
    }

    private static XmlWhitespaceSensitivity? RemoveXmlWhitespaceSensitivity(
        ConfigurationResolutionContext context,
        string propertyName
    )
    {
        switch (context.RemoveString(propertyName))
        {
            case null:
                return null;
            case "strict":
                return XmlWhitespaceSensitivity.Strict;
            case "ignore":
                return XmlWhitespaceSensitivity.Ignore;
            case var value:
                context.AddDiagnostic(propertyName, $"Unknown XML whitespace sensitivity: {value}");
                return null;
        }
    }
}
