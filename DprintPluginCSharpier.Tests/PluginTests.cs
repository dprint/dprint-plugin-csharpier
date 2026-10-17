using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Dprint.Plugins.CSharpier;

[TestFixture]
public class PluginTests
{
  [Test]
  public void GetPluginInfo_Gets()
  {
    var json = Encoding.UTF8.GetString(new Plugin().GetPluginInfo());
    Assert.That(json, Does.StartWith("""{"name":"dprint-plugin-csharpier","version":"""));
    Assert.That(
      json,
      Does.Match(
        """
        "version":"\d+\.\d+\.\d+"
        """.Trim()
      )
    );
    Assert.That(
      json,
      Does.Contain(
        """
        "configKey":"csharpier"
        """.Trim()
      )
    );
    Assert.That(
      json,
      Does.Match(
        """
        "configSchemaUrl":"https://plugins\.dprint\.dev/dprint/dprint-plugin-csharpier/\d+\.\d+\.\d+/schema\.json"
        """.Trim()
      )
    );
  }

  [Test]
  public void GetLicenseText_Gets()
  {
    var licenseText = new Plugin().GetLicenseText();
    Assert.That(licenseText, Does.StartWith("CSharpier: MIT - Copyright (c) 2021 belav"));
    Assert.That(licenseText, Does.Contain("The MIT License (MIT)"));
  }

  [Test]
  public void GetFileMatchingInfo_Gets()
  {
    Assert.That(
      ToString(new Plugin().GetFileMatchingInfo()),
      Is.EqualTo(
        """{"fileExtensions":["cs","csx","csproj","props","targets","slnx","config","xml","xaml","axaml"],"fileNames":[]}"""
      )
    );
  }

  [Test]
  public void RegisterConfig_GlobalAndPluginConfig_Resolves()
  {
    var plugin = new Plugin();
    plugin.RegisterConfig(
      1,
      ToBytes(
        """
        {
          "global": { "lineWidth": 80, "indentWidth": 3, "useTabs": null, "newLineKind": "lf" },
          "plugin": { "csharp.useTabs": true, "xml.indentWidth": 2, "xmlWhitespaceSensitivity": "ignore" }
        }
        """
      )
    );

    Assert.That(ToString(plugin.GetConfigDiagnostics(1)), Is.EqualTo("[]"));
    Assert.That(
      ToString(plugin.GetResolvedConfig(1)),
      Is.EqualTo(
        """
        {"csharp.lineWidth":80,"csharp.indentWidth":3,"csharp.useTabs":true,"csharp.newLineKind":"lf",
        "xml.lineWidth":80,"xml.indentWidth":2,"xml.useTabs":false,"xml.newLineKind":"lf",
        "includeGenerated":false,"xmlWhitespaceSensitivity":"ignore"}
        """.ReplaceLineEndings("")
      )
    );
  }

  [Test]
  public void GetConfigDiagnostics_UnknownProperty_Diagnostic()
  {
    var plugin = new Plugin();
    plugin.RegisterConfig(1, ToBytes("""{ "global": {}, "plugin": { "unknownProp": [1] } }"""));

    Assert.That(
      ToString(plugin.GetConfigDiagnostics(1)),
      Is.EqualTo(
        """[{"propertyName":"unknownProp","message":"Unknown configuration property name."}]"""
      )
    );
  }

  [Test]
  public void GetConfigDiagnostics_ReleasedConfig_Throws()
  {
    var plugin = new Plugin();
    plugin.RegisterConfig(1, ToBytes("""{ "global": {}, "plugin": {} }"""));
    plugin.ReleaseConfig(1);

    Assert.That(
      () => plugin.GetConfigDiagnostics(1),
      Throws.Exception.With.Message.StartsWith("Could not find configuration id: 1")
    );
  }

  [Test]
  public void Format_CSharpAndXml_Formats()
  {
    var plugin = CreatePluginWithDefaultConfig();

    Assert.That(
      Format(plugin, "file.cs", "namespace Test    {   class A{}  }\n"),
      Is.EqualTo("namespace Test\n{\n    class A { }\n}\n")
    );
    Assert.That(Format(plugin, "file.cs", "namespace Test\n{\n    class A { }\n}\n"), Is.Null);
    Assert.That(
      Format(plugin, "file.csx", "#r \"nuget: Foo, 1.0\"\nvar x=1;\n"),
      Is.EqualTo("#r \"nuget: Foo, 1.0\"\nvar x = 1;\n")
    );
    Assert.That(
      Format(plugin, "file.csproj", "<Project><PropertyGroup></PropertyGroup></Project>\n"),
      Is.EqualTo("<Project>\n  <PropertyGroup></PropertyGroup>\n</Project>\n")
    );
  }

  [Test]
  public void Format_OverrideConfig_UsesOverride()
  {
    var plugin = CreatePluginWithDefaultConfig();
    var text = "class Test\n{\n    void Method() { }\n}\n";

    var result = plugin.Format(
      1,
      "file.cs",
      ToBytes(text),
      null,
      ToBytes("""{ "csharp.indentWidth": 2 }""")
    );

    Assert.That(ToString(result!), Is.EqualTo("class Test\n{\n  void Method() { }\n}\n"));
    // the override shouldn't be remembered
    Assert.That(Format(plugin, "file.cs", text), Is.Null);
  }

  [Test]
  public void Format_Range_NoChange()
  {
    var plugin = CreatePluginWithDefaultConfig();
    var text = "class   Test{}\n";

    Assert.That(plugin.Format(1, "file.cs", ToBytes(text), (0, 5), null), Is.Null);
    // the whole file
    Assert.That(
      ToString(plugin.Format(1, "file.cs", ToBytes(text), (0, text.Length), null)!),
      Is.EqualTo("class Test { }\n")
    );
  }

  [Test]
  public void Format_WithBom_PreservesBom()
  {
    var plugin = CreatePluginWithDefaultConfig();
    var bom = Encoding.UTF8.GetPreamble();

    var result = plugin.Format(
      1,
      "file.cs",
      bom.Concat(ToBytes("class   Test{}\n")).ToArray(),
      null,
      null
    );

    Assert.That(result, Is.EqualTo(bom.Concat(ToBytes("class Test { }\n")).ToArray()));
    Assert.That(plugin.Format(1, "file.cs", result!, null, null), Is.Null);
  }

  [Test]
  public void Format_Utf16_KeepsEncoding()
  {
    var plugin = CreatePluginWithDefaultConfig();
    var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

    var result = plugin.Format(
      1,
      "file.cs",
      encoding.GetPreamble().Concat(encoding.GetBytes("class   Test{}\n")).ToArray(),
      null,
      null
    );

    Assert.That(
      result,
      Is.EqualTo(encoding.GetPreamble().Concat(encoding.GetBytes("class Test { }\n")).ToArray())
    );
  }

  [Test]
  public void Format_UnknownExtension_Throws()
  {
    var plugin = CreatePluginWithDefaultConfig();

    Assert.That(
      () => Format(plugin, "file.txt", "text"),
      Throws.Exception.With.Message.EqualTo("Could not find formatter for file path: file.txt")
    );
  }

  private static Plugin CreatePluginWithDefaultConfig()
  {
    var plugin = new Plugin();
    plugin.RegisterConfig(1, ToBytes("""{ "global": {}, "plugin": { "newLineKind": "lf" } }"""));
    return plugin;
  }

  private static string? Format(Plugin plugin, string filePath, string text)
  {
    var result = plugin.Format(1, filePath, ToBytes(text), null, null);
    return result is null ? null : ToString(result);
  }

  private static byte[] ToBytes(string text) => Encoding.UTF8.GetBytes(text);

  private static string ToString(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
