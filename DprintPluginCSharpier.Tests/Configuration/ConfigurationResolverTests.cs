using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Dprint.Plugins.CSharpier.Configuration;

[TestFixture]
public class ConfigurationResolverTests
{
    [Test]
    public void Resolve_Default_UsesCSharpierDefaults()
    {
        var (config, diagnostics) = ConfigurationResolver.Resolve(
            new(),
            new Dictionary<string, object>()
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            config.CSharp,
            Is.EqualTo(new LanguageConfiguration(100, 4, false, NewLineKind.Auto))
        );
        Assert.That(
            config.Xml,
            Is.EqualTo(new LanguageConfiguration(100, 2, false, NewLineKind.Auto))
        );
        Assert.That(config.IncludeGenerated, Is.False);
        Assert.That(config.XmlWhitespaceSensitivity, Is.Null);
        Assert.That(config.ToDictionary().ContainsKey("xmlWhitespaceSensitivity"), Is.False);
    }

    [Test]
    public void Resolve_GlobalConfig_AppliesToAllLanguages()
    {
        var globalConfig = new GlobalConfiguration
        {
            LineWidth = 80,
            IndentWidth = 3,
            UseTabs = true,
            NewLineKind = "crlf",
        };

        var (config, diagnostics) = ConfigurationResolver.Resolve(
            globalConfig,
            new Dictionary<string, object>()
        );

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            config.CSharp,
            Is.EqualTo(new LanguageConfiguration(80, 3, true, NewLineKind.Crlf))
        );
        Assert.That(
            config.Xml,
            Is.EqualTo(new LanguageConfiguration(80, 3, true, NewLineKind.Crlf))
        );
    }

    [Test]
    public void Resolve_PluginConfig_OverridesGlobalConfig()
    {
        var globalConfig = new GlobalConfiguration
        {
            LineWidth = 80,
            IndentWidth = 3,
            UseTabs = true,
            NewLineKind = "crlf",
        };
        var pluginConfig = new Dictionary<string, object>
        {
            ["lineWidth"] = 120,
            ["indentWidth"] = 4,
            ["useTabs"] = false,
            ["newLineKind"] = "lf",
            ["includeGenerated"] = true,
            ["xmlWhitespaceSensitivity"] = "strict",
        };

        var (config, diagnostics) = ConfigurationResolver.Resolve(globalConfig, pluginConfig);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            config.CSharp,
            Is.EqualTo(new LanguageConfiguration(120, 4, false, NewLineKind.Lf))
        );
        Assert.That(
            config.Xml,
            Is.EqualTo(new LanguageConfiguration(120, 4, false, NewLineKind.Lf))
        );
        Assert.That(config.IncludeGenerated, Is.True);
        Assert.That(
            config.XmlWhitespaceSensitivity,
            Is.EqualTo(global::CSharpier.Core.XmlWhitespaceSensitivity.Strict)
        );
    }

    [Test]
    public void Resolve_LanguageConfig_OverridesPluginConfig()
    {
        var pluginConfig = new Dictionary<string, object>
        {
            ["indentWidth"] = 3,
            ["csharp.lineWidth"] = 120,
            ["csharp.useTabs"] = true,
            ["xml.indentWidth"] = 2,
            ["xml.newLineKind"] = "crlf",
        };

        var (config, diagnostics) = ConfigurationResolver.Resolve(new(), pluginConfig);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(
            config.CSharp,
            Is.EqualTo(new LanguageConfiguration(120, 3, true, NewLineKind.Auto))
        );
        Assert.That(
            config.Xml,
            Is.EqualTo(new LanguageConfiguration(100, 2, false, NewLineKind.Crlf))
        );
    }

    [Test]
    public void Resolve_InvalidValues_Diagnostics()
    {
        var pluginConfig = new Dictionary<string, object>
        {
            ["lineWidth"] = 0,
            ["csharp.indentWidth"] = "4",
            ["useTabs"] = 1,
            ["xml.newLineKind"] = "cr",
            ["xmlWhitespaceSensitivity"] = "loose",
            ["unknownProp"] = true,
        };

        var (config, diagnostics) = ConfigurationResolver.Resolve(new(), pluginConfig);

        Assert.That(
            diagnostics.Select(d => (d.PropertyName, d.Message)),
            Is.EquivalentTo(
                new[]
                {
                    ("lineWidth", "Value must be greater than 0."),
                    ("csharp.indentWidth", "Value must be a Int32"),
                    ("useTabs", "Value must be a Boolean"),
                    ("xml.newLineKind", "Unknown new line kind: cr"),
                    ("xmlWhitespaceSensitivity", "Unknown XML whitespace sensitivity: loose"),
                    ("unknownProp", "Unknown configuration property name."),
                }
            )
        );
        // falls back to the defaults
        Assert.That(
            config.CSharp,
            Is.EqualTo(new LanguageConfiguration(100, 4, false, NewLineKind.Auto))
        );
        Assert.That(
            config.Xml,
            Is.EqualTo(new LanguageConfiguration(100, 2, false, NewLineKind.Auto))
        );
    }

    [TestCase(NewLineKind.Auto, "a\r\nb\nc", "\n")]
    [TestCase(NewLineKind.Auto, "a\nb\r\nc", "\r\n")]
    [TestCase(NewLineKind.Auto, "a", "\n")]
    [TestCase(NewLineKind.Lf, "a\r\n", "\n")]
    [TestCase(NewLineKind.Crlf, "a\n", "\r\n")]
    public void GetNewLine_Gets(NewLineKind newLineKind, string text, string expected)
    {
        var config = new LanguageConfiguration(100, 4, false, newLineKind);

        Assert.That(config.GetNewLine(text), Is.EqualTo(expected));
    }
}
