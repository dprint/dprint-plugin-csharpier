using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Dprint.Plugins.CSharpier.Wasm;

/// <summary>
/// Tests that run the plugin compiled to Wasm in dprint.
///
/// These only run when the <c>DPRINT_CSHARPIER_WASM_PATH</c> environment variable is set to the
/// path of the built .wasm file. They use the dprint executable at <c>DPRINT_PATH</c> or
/// otherwise the one on the path.
/// </summary>
[TestFixture]
[Category("Wasm")]
public class WasmPluginTests
{
    private const string DefaultConfig = """
        "newLineKind": "lf"
        """;

    private string _wasmPath = "";
    private string _cacheDir = "";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var wasmPath = Environment.GetEnvironmentVariable("DPRINT_CSHARPIER_WASM_PATH");
        if (string.IsNullOrEmpty(wasmPath))
            Assert.Ignore(
                "Set the DPRINT_CSHARPIER_WASM_PATH environment variable to run the Wasm tests."
            );
        _wasmPath = Path.GetFullPath(wasmPath);
        Assert.That(File.Exists(_wasmPath), $"Could not find Wasm file: {_wasmPath}");
        // share the cache directory so the plugin is only compiled once
        _cacheDir = Directory.CreateTempSubdirectory("dprint-plugin-csharpier-cache").FullName;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (_cacheDir.Length > 0)
            DprintDirectory.DeleteDirectory(_cacheDir);
    }

    [Test]
    public void Fmt_CSharpAndXml_Formats()
    {
        using var dir = CreateDirectory(DefaultConfig);
        dir.WriteFile("file.cs", "namespace Test    {   class A{}  }\n");
        dir.WriteFile("formatted.cs", "namespace Test;\n\nclass A { }\n");
        dir.WriteFile("script.csx", "#r \"nuget: Foo, 1.0\"\nvar x=1;\n");
        dir.WriteFile("file.csproj", "<Project><PropertyGroup></PropertyGroup></Project>\n");
        dir.WriteFile("file.xaml", "<Window><TextBlock>  Some   text  </TextBlock></Window>\n");

        var check = dir.Run("check");
        Assert.That(check.ExitCode, Is.Not.Zero);
        Assert.That(check.Output, Does.Contain("Found 4 not formatted files."));

        Assert.That(dir.Run("fmt").ExitCode, Is.Zero);
        Assert.That(dir.ReadFile("file.cs"), Is.EqualTo("namespace Test\n{\n    class A { }\n}\n"));
        Assert.That(dir.ReadFile("formatted.cs"), Is.EqualTo("namespace Test;\n\nclass A { }\n"));
        Assert.That(dir.ReadFile("script.csx"), Is.EqualTo("#r \"nuget: Foo, 1.0\"\nvar x = 1;\n"));
        Assert.That(
            dir.ReadFile("file.csproj"),
            Is.EqualTo("<Project>\n  <PropertyGroup></PropertyGroup>\n</Project>\n")
        );
        Assert.That(
            dir.ReadFile("file.xaml"),
            Is.EqualTo("<Window>\n  <TextBlock>Some text</TextBlock>\n</Window>\n")
        );
        Assert.That(dir.Run("check").ExitCode, Is.Zero);
    }

    [Test]
    public void Fmt_Config_UsesConfig()
    {
        using var dir = CreateDirectory(
            """
            "lineWidth": 20,
            "indentWidth": 3,
            "newLineKind": "lf",
            "csharpier": {
              "csharp.useTabs": true,
              "xml.indentWidth": 4
            }
            """
        );
        dir.WriteFile("file.cs", "class A { void M() { Method(argumentOne, argumentTwo); } }\n");
        dir.WriteFile("file.xml", "<a><b /></a>\n");

        Assert.That(dir.Run("fmt").ExitCode, Is.Zero);
        Assert.That(
            dir.ReadFile("file.cs"),
            Is.EqualTo(
                "class A\n{\n\tvoid M()\n\t{\n\t\tMethod(\n\t\t\targumentOne,\n\t\t\targumentTwo\n\t\t);\n\t}\n}\n"
            )
        );
        Assert.That(dir.ReadFile("file.xml"), Is.EqualTo("<a>\n    <b />\n</a>\n"));
    }

    [Test]
    public void Fmt_NoNewLineKind_UsesLineEndingsOfFile()
    {
        using var dir = CreateDirectory("");
        dir.WriteFile("crlf.cs", "class Test\r\n{\r\nvoid Method() { }\r\n}\r\n");
        dir.WriteFile("lf.cs", "class Test\n{\nvoid Method() { }\n}\n");

        Assert.That(dir.Run("fmt").ExitCode, Is.Zero);
        Assert.That(
            dir.ReadFile("crlf.cs"),
            Is.EqualTo("class Test\r\n{\r\n    void Method() { }\r\n}\r\n")
        );
        Assert.That(dir.ReadFile("lf.cs"), Is.EqualTo("class Test\n{\n    void Method() { }\n}\n"));
    }

    [Test]
    public void Fmt_NewLineKind_ChangesAllLineEndings()
    {
        using var dir = CreateDirectory(
            """
            "newLineKind": "crlf"
            """
        );
        dir.WriteFile("file.cs", "class Test\n{\n/* a\nb */\nstring s = @\"a\nb\";\n}\n");
        dir.WriteFile("file.csproj", "<Project>\n<!-- a\nb -->\n</Project>\n");

        Assert.That(dir.Run("fmt").ExitCode, Is.Zero);
        Assert.That(
            dir.ReadFile("file.cs"),
            Is.EqualTo(
                "class Test\r\n{\r\n    /* a\r\n    b */\r\n    string s =\r\n        @\"a\r\nb\";\r\n}\r\n"
            )
        );
        Assert.That(
            dir.ReadFile("file.csproj"),
            Is.EqualTo("<Project>\r\n  <!-- a\r\nb -->\r\n</Project>\r\n")
        );
        Assert.That(dir.Run("check").ExitCode, Is.Zero);
    }

    [Test]
    public void Fmt_GeneratedFiles_Skips()
    {
        using var dir = CreateDirectory(DefaultConfig);
        dir.WriteFile("file.g.cs", "class   A{}\n");
        dir.WriteFile("file.cs", "// <auto-generated />\nclass   A{}\n");

        Assert.That(dir.Run("fmt").ExitCode, Is.Zero);
        Assert.That(dir.ReadFile("file.g.cs"), Is.EqualTo("class   A{}\n"));
        Assert.That(dir.ReadFile("file.cs"), Is.EqualTo("// <auto-generated />\nclass   A{}\n"));
    }

    [Test]
    public void Fmt_ConfigDiagnostics_Errors()
    {
        using var dir = CreateDirectory(
            """
            "csharpier": { "unknownProp": true, "csharp.useTabs": "test" }
            """
        );
        dir.WriteFile("file.cs", "class   A{}\n");

        var result = dir.Run("fmt");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(
            result.Output,
            Does.Contain(
                "[dprint-plugin-csharpier]: Unknown configuration property name. (unknownProp)"
            )
        );
        Assert.That(result.Output, Does.Contain("(csharp.useTabs)"));
        Assert.That(dir.ReadFile("file.cs"), Is.EqualTo("class   A{}\n"));
    }

    [Test]
    public void Fmt_SyntaxErrors_Errors()
    {
        using var dir = CreateDirectory(DefaultConfig);
        dir.WriteFile("file.cs", "class A {\n");
        dir.WriteFile("file.xml", "<a><b>\n");
        dir.WriteFile(
            "deep.cs",
            $"class A {{ int x = {string.Join(" + ", Enumerable.Repeat("1", 300))}; }}\n"
        );

        var result = dir.Run("fmt");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(
            result.Output,
            Does.Contain(
                "Was not formatted due to syntax errors.\n(1,10): error CS1513: } expected"
            )
        );
        Assert.That(
            result.Output,
            Does.Contain("error XML001: Unexpected end of file has occurred.")
        );
        Assert.That(result.Output, Does.Contain("We can't handle this deep of recursion yet."));
        Assert.That(dir.ReadFile("file.cs"), Is.EqualTo("class A {\n"));
        Assert.That(dir.ReadFile("file.xml"), Is.EqualTo("<a><b>\n"));
        Assert.That(dir.ReadFile("deep.cs"), Does.StartWith("class A {"));
    }

    [Test]
    public void OutputResolvedConfig_Outputs()
    {
        using var dir = CreateDirectory(
            """
            "indentWidth": 3,
            "newLineKind": "crlf",
            "csharpier": { "csharp.lineWidth": 120, "xml.indentWidth": 2 }
            """
        );

        var result = dir.Run("output-resolved-config");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output, Does.Contain("\"csharp.lineWidth\": 120"));
        Assert.That(result.Output, Does.Contain("\"csharp.indentWidth\": 3"));
        Assert.That(result.Output, Does.Contain("\"csharp.newLineKind\": \"crlf\""));
        Assert.That(result.Output, Does.Contain("\"xml.lineWidth\": 100"));
        Assert.That(result.Output, Does.Contain("\"xml.indentWidth\": 2"));
        Assert.That(result.Output, Does.Contain("\"includeGenerated\": false"));
    }

    [Test]
    public void License_OutputsLicense()
    {
        using var dir = CreateDirectory(DefaultConfig);

        var result = dir.Run("license");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output, Does.Contain("==== DPRINT-PLUGIN-CSHARPIER LICENSE ===="));
        Assert.That(
            result.Output,
            Does.Contain(new Plugin().GetLicenseText().Split('\n')[0].Trim())
        );
    }

    [Test]
    public void Fmt_Encodings_KeepsEncoding()
    {
        using var dir = CreateDirectory(DefaultConfig);
        var text = "class Test    {   }\n// é\n";
        var expectedText = "class Test { }\n// é\n";
        var utf8Bom = Encoding.UTF8.GetPreamble();
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var utf16BigEndian = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        dir.WriteFile("bom.cs", [.. utf8Bom, .. Encoding.UTF8.GetBytes(text)]);
        dir.WriteFile("utf16.cs", [.. utf16.GetPreamble(), .. utf16.GetBytes(text)]);
        dir.WriteFile(
            "utf16be.cs",
            [.. utf16BigEndian.GetPreamble(), .. utf16BigEndian.GetBytes(text)]
        );

        var result = dir.Run("fmt");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(
            dir.ReadFileBytes("bom.cs"),
            Is.EqualTo((byte[])[.. utf8Bom, .. Encoding.UTF8.GetBytes(expectedText)])
        );
        Assert.That(
            dir.ReadFileBytes("utf16.cs"),
            Is.EqualTo((byte[])[.. utf16.GetPreamble(), .. utf16.GetBytes(expectedText)])
        );
        Assert.That(
            dir.ReadFileBytes("utf16be.cs"),
            Is.EqualTo(
                (byte[])[.. utf16BigEndian.GetPreamble(), .. utf16BigEndian.GetBytes(expectedText)]
            )
        );
        Assert.That(dir.Run("check").ExitCode, Is.Zero);
    }

    [Test]
    public void Fmt_InvalidUtf8_Errors()
    {
        using var dir = CreateDirectory(DefaultConfig);
        dir.WriteFile(
            "file.cs",
            [.. Encoding.UTF8.GetBytes("class Test    { } // "), 0xFF, (byte)'\n']
        );

        var result = dir.Run("fmt");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Does.Contain("Error formatting"));
    }

    [Test]
    public void EditorService_CanFormat_CanFormat()
    {
        using var dir = CreateDirectory(DefaultConfig);
        using var editorService = dir.StartEditorService();

        foreach (var extension in Plugin.FileExtensions)
            Assert.That(
                editorService.CanFormat(dir.GetPath($"file.{extension}")),
                Is.True,
                extension
            );
    }

    [Test]
    public void EditorService_Format_Formats()
    {
        using var dir = CreateDirectory(DefaultConfig);
        using var editorService = dir.StartEditorService();

        Assert.That(
            editorService.FormatText(dir.GetPath("file.cs"), "class   Test{}\n"),
            Is.EqualTo("class Test { }\n")
        );
        Assert.That(editorService.FormatText(dir.GetPath("file.cs"), "class Test { }\n"), Is.Null);
        Assert.That(
            editorService.FormatText(
                dir.GetPath("file.props"),
                "<Project><PropertyGroup /></Project>\n"
            ),
            Is.EqualTo("<Project>\n  <PropertyGroup />\n</Project>\n")
        );
        // not a file the plugin formats
        Assert.That(editorService.FormatText(dir.GetPath("file.txt"), "class   Test{}\n"), Is.Null);
    }

    [Test]
    public void EditorService_FormatRange_NoChange()
    {
        using var dir = CreateDirectory(DefaultConfig);
        using var editorService = dir.StartEditorService();
        var text = "class   Test{}\nclass   Other{}\n";

        Assert.That(
            editorService.FormatText(dir.GetPath("file.cs"), text, (0, "class   Test{}".Length)),
            Is.Null
        );
    }

    [Test]
    public void EditorService_OverrideConfig_UsesOverride()
    {
        using var dir = CreateDirectory(DefaultConfig);
        using var editorService = dir.StartEditorService();
        var filePath = dir.GetPath("file.cs");
        var text = "class Test\n{\n    void Method() { }\n}\n";

        Assert.That(
            editorService.FormatText(
                filePath,
                text,
                overrideConfigJson: """{ "csharp.indentWidth": 2 }"""
            ),
            Is.EqualTo("class Test\n{\n  void Method() { }\n}\n")
        );
        // the override isn't remembered
        Assert.That(editorService.FormatText(filePath, text), Is.Null);
    }

    /// <summary>
    /// Ensures the Wasm build gives the same result as running the plugin's code on
    /// the .NET runtime by formatting the code in this repository with both.
    /// </summary>
    [Test]
    public void EditorService_RepositoryCode_SameAsDotNetRuntime()
    {
        using var dir = CreateDirectory(DefaultConfig);
        using var editorService = dir.StartEditorService();
        var plugin = new Plugin();
        plugin.RegisterConfig(
            1,
            Encoding.UTF8.GetBytes("""{ "global": { "newLineKind": "lf" }, "plugin": {} }""")
        );
        var filePaths = GetRepositoryCodeFilePaths().ToList();
        Assert.That(filePaths, Has.Count.GreaterThan(10));

        foreach (var filePath in filePaths)
        {
            var fileName = $"file{Path.GetExtension(filePath)}";
            var originalBytes = File.ReadAllBytes(filePath);
            foreach (var (description, fileBytes) in GetUnformattedVariations(originalBytes))
            {
                var expected = plugin.Format(1, fileName, fileBytes, null, null);
                var actual = editorService.Format(dir.GetPath(fileName), fileBytes);
                Assert.That(
                    ToText(actual),
                    Is.EqualTo(ToText(expected)),
                    $"{filePath} ({description})"
                );
            }
        }
    }

    private DprintDirectory CreateDirectory(string config)
    {
        return new DprintDirectory(_wasmPath, _cacheDir, config);
    }

    private static IEnumerable<(string Description, byte[] Bytes)> GetUnformattedVariations(
        byte[] fileBytes
    )
    {
        yield return ("original", fileBytes);
        var bom = Encoding.UTF8.GetPreamble();
        var hasBom = fileBytes.AsSpan().StartsWith(bom);
        var text = Encoding
            .UTF8.GetString(hasBom ? fileBytes[bom.Length..] : fileBytes)
            .ReplaceLineEndings("\n");
        var lines = text.Split('\n');
        yield return (
            "no indentation",
            ToBytes(string.Join("\n", lines.Select(line => line.TrimStart())))
        );
        yield return (
            "extra indentation",
            ToBytes(string.Join("\n", lines.Select(line => line.Length == 0 ? line : "   " + line)))
        );
        yield return ("crlf", ToBytes(text.Replace("\n", "\r\n")));

        byte[] ToBytes(string text) =>
            hasBom ? [.. bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);
    }

    private static IEnumerable<string> GetRepositoryCodeFilePaths()
    {
        var rootDir = GetRepositoryRootDir();
        foreach (
            var projectDirName in new[] { "DprintPluginCSharpier", "DprintPluginCSharpier.Tests" }
        )
        {
            var projectDir = Path.Combine(rootDir, projectDirName);
            foreach (
                var filePath in Directory.EnumerateFiles(
                    projectDir,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                var relativePath = Path.GetRelativePath(projectDir, filePath).Replace('\\', '/');
                var extension = Path.GetExtension(filePath).TrimStart('.');
                if (
                    !relativePath.StartsWith("bin/")
                    && !relativePath.StartsWith("obj/")
                    && Plugin.FileExtensions.Contains(extension)
                )
                    yield return filePath;
            }
        }
    }

    private static string GetRepositoryRootDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DprintPluginCSharpier.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new Exception("Could not find the root directory of the repository.");
    }

    private static string? ToText(byte[]? bytes) =>
        bytes is null ? null : Encoding.UTF8.GetString(bytes);
}
