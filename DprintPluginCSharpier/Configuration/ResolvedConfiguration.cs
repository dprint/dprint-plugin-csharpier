using System;
using System.Collections.Generic;
using CSharpier.Core;

namespace Dprint.Plugins.CSharpier.Configuration;

/// <summary>
/// The configuration used to format files.
/// </summary>
public class ResolvedConfiguration
{
  public LanguageConfiguration CSharp { get; init; } = null!;
  public LanguageConfiguration Xml { get; init; } = null!;
  public bool IncludeGenerated { get; init; }

  /// <summary>
  /// The whitespace sensitivity of XML files or null to use CSharpier's default for the file extension.
  /// </summary>
  public XmlWhitespaceSensitivity? XmlWhitespaceSensitivity { get; init; }

  public Dictionary<string, object> ToDictionary()
  {
    var config = new Dictionary<string, object>();
    AddLanguageConfig("csharp.", CSharp);
    AddLanguageConfig("xml.", Xml);
    config["includeGenerated"] = IncludeGenerated;
    if (XmlWhitespaceSensitivity is { } xmlWhitespaceSensitivity)
      config["xmlWhitespaceSensitivity"] = xmlWhitespaceSensitivity;
    return config;

    void AddLanguageConfig(string prefix, LanguageConfiguration languageConfig)
    {
      config[$"{prefix}lineWidth"] = languageConfig.LineWidth;
      config[$"{prefix}indentWidth"] = languageConfig.IndentWidth;
      config[$"{prefix}useTabs"] = languageConfig.UseTabs;
      config[$"{prefix}newLineKind"] = languageConfig.NewLineKind;
    }
  }
}

public record LanguageConfiguration(
  int LineWidth,
  int IndentWidth,
  bool UseTabs,
  NewLineKind NewLineKind
)
{
  /// <summary>
  /// Gets the new line to use for the text with "auto" resolved to the last line ending in the text.
  /// </summary>
  public string GetNewLine(string text)
  {
    switch (NewLineKind)
    {
      case NewLineKind.Lf:
        return "\n";
      case NewLineKind.Crlf:
        return "\r\n";
      case NewLineKind.System:
        return Environment.NewLine;
      default:
        var index = text.LastIndexOf('\n');
        return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
    }
  }
}

public enum NewLineKind
{
  Auto,
  Lf,
  Crlf,
  System,
}
