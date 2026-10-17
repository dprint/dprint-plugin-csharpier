using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Dprint.Plugins.CSharpier.Configuration;
using NUnit.Framework;

namespace Dprint.Plugins.CSharpier.Formatters;

[TestFixture]
public class FileFormatterTests
{
    [Test]
    public void Format_PluginFileExtensions_FormattedByCSharpier()
    {
        var formatter = CreateFormatter();

        foreach (var extension in Plugin.FileExtensions)
        {
            var input =
                extension.StartsWith("cs") && extension != "csproj"
                    ? "class   A{}\n"
                    : "<a><b /></a>\n";
            Assert.That(Format(formatter, $"file.{extension}", input), Is.Not.Null, extension);
        }
    }

    [TestCase("\r\n")]
    [TestCase("\n")]
    public void Format_DefaultNewLineKind_UsesLineEndingsOfFile(string newLine)
    {
        var formatter = CreateFormatter();

        var result = Format(
            formatter,
            "file.cs",
            $"class A{newLine}{{{newLine}void M() {{ }}{newLine}}}{newLine}"
        );

        Assert.That(
            result,
            Is.EqualTo($"class A{newLine}{{{newLine}    void M() {{ }}{newLine}}}{newLine}")
        );
    }

    [TestCase("class A\n{\r\n}\r\n", "class A { }\r\n")]
    [TestCase("class A\r\n{\r\n}\n", "class A { }\n")]
    public void Format_MixedLineEndings_UsesLastLineEndingOfFile(string input, string expected)
    {
        var formatter = CreateFormatter();

        Assert.That(Format(formatter, "file.cs", input), Is.EqualTo(expected));
    }

    [TestCase("lf", "\n")]
    [TestCase("crlf", "\r\n")]
    public void Format_NewLineKind_UsesItForAllLines(string newLineKind, string expectedNewLine)
    {
        var formatter = CreateFormatter(new() { ["newLineKind"] = newLineKind });

        foreach (var fileNewLine in new[] { "\n", "\r\n" })
        {
            // includes the new lines in comments and strings
            var input = string.Join(
                fileNewLine,
                "class A",
                "{",
                "/* a",
                "b */",
                "string s = @\"a",
                "b\";",
                "}",
                ""
            );
            var expected = string.Join(
                expectedNewLine,
                "class A",
                "{",
                "    /* a",
                "    b */",
                "    string s =",
                "        @\"a",
                "b\";",
                "}",
                ""
            );
            Assert.That(Format(formatter, "file.cs", input) ?? input, Is.EqualTo(expected));
            var xmlInput = string.Join(
                fileNewLine,
                "<Project>",
                "<!-- a",
                "b -->",
                "</Project>",
                ""
            );
            var xmlExpected = string.Join(
                expectedNewLine,
                "<Project>",
                "  <!-- a",
                "b -->",
                "</Project>",
                ""
            );
            Assert.That(
                Format(formatter, "file.csproj", xmlInput) ?? xmlInput,
                Is.EqualTo(xmlExpected)
            );
        }
    }

    [Test]
    public void Format_Config_UsesLanguageConfig()
    {
        var formatter = CreateFormatter(
            new()
            {
                ["newLineKind"] = "lf",
                ["csharp.lineWidth"] = 20,
                ["csharp.useTabs"] = true,
                ["xml.indentWidth"] = 4,
            }
        );

        Assert.That(
            Format(
                formatter,
                "file.cs",
                "class A { void M() { Method(argumentOne, argumentTwo); } }\n"
            ),
            Is.EqualTo(
                "class A\n{\n\tvoid M()\n\t{\n\t\tMethod(\n\t\t\targumentOne,\n\t\t\targumentTwo\n\t\t);\n\t}\n}\n"
            )
        );
        Assert.That(
            Format(formatter, "file.xml", "<a><b /></a>\n"),
            Is.EqualTo("<a>\n    <b />\n</a>\n")
        );
    }

    [Test]
    public void Format_XmlWhitespaceSensitivity_DefaultsBasedOnExtension()
    {
        var input = "<a><b>  text  </b></a>\n";
        var strictOutput = "<a>\n  <b>  text  </b>\n</a>\n";
        var ignoreOutput = "<a>\n  <b>text</b>\n</a>\n";

        var formatter = CreateFormatter(new() { ["newLineKind"] = "lf" });
        Assert.That(Format(formatter, "file.xml", input), Is.EqualTo(strictOutput));
        Assert.That(Format(formatter, "file.xaml", input), Is.EqualTo(ignoreOutput));

        formatter = CreateFormatter(
            new() { ["newLineKind"] = "lf", ["xmlWhitespaceSensitivity"] = "ignore" }
        );
        Assert.That(Format(formatter, "file.xml", input), Is.EqualTo(ignoreOutput));

        formatter = CreateFormatter(
            new() { ["newLineKind"] = "lf", ["xmlWhitespaceSensitivity"] = "strict" }
        );
        Assert.That(Format(formatter, "file.xaml", input), Is.EqualTo(strictOutput));
    }

    [TestCase("file.g.cs")]
    [TestCase("file.designer.cs")]
    [TestCase("TemporaryGeneratedFile_file.cs")]
    public void Format_GeneratedFileName_SkipsUnlessIncludeGenerated(string fileName)
    {
        var input = "class   A{}\n";

        Assert.That(Format(CreateFormatter(), fileName, input), Is.Null);
        Assert.That(
            Format(CreateFormatter(new() { ["includeGenerated"] = true }), fileName, input),
            Is.EqualTo("class A { }\n")
        );
    }

    [Test]
    public void Format_AutoGeneratedComment_SkipsUnlessIncludeGenerated()
    {
        var input = "// <auto-generated />\nclass   A{}\n";

        Assert.That(Format(CreateFormatter(), "file.cs", input), Is.Null);
        Assert.That(
            Format(CreateFormatter(new() { ["includeGenerated"] = true }), "file.cs", input),
            Is.EqualTo("// <auto-generated />\nclass A { }\n")
        );
    }

    [Test]
    public void Format_CSharpSyntaxError_Throws()
    {
        var formatter = CreateFormatter();

        Assert.That(
            () => Format(formatter, "file.cs", "class A {\n"),
            Throws.Exception.With.Message.EqualTo(
                "Was not formatted due to syntax errors.\n(1,10): error CS1513: } expected"
            )
        );
    }

    [Test]
    public void Format_XmlSyntaxError_Throws()
    {
        var formatter = CreateFormatter();

        Assert.That(
            () => Format(formatter, "file.xml", "<a><b>\n"),
            Throws.Exception.With.Message.StartsWith(
                "Was not formatted due to syntax errors.\n(2,1): error XML001: Unexpected end of file has occurred."
            )
        );
    }

    [Test]
    public void Format_TooDeep_Throws()
    {
        var formatter = CreateFormatter();
        // CSharpier gives up on deeply nested binary expressions and returns empty code
        var input = $"class A {{ int x = {string.Join(" + ", Enumerable.Repeat("1", 300))}; }}\n";

        Assert.That(
            () => Format(formatter, "file.cs", input),
            Throws.Exception.With.Message.EqualTo("We can't handle this deep of recursion yet.")
        );
    }

    private static FileFormatter CreateFormatter(Dictionary<string, object>? pluginConfig = null)
    {
        var (config, diagnostics) = ConfigurationResolver.Resolve(new(), pluginConfig ?? new());
        Assert.That(diagnostics, Is.Empty);
        return new FileFormatter(config);
    }

    private static string? Format(FileFormatter formatter, string filePath, string text)
    {
        var result = formatter.Format(
            filePath,
            Encoding.UTF8.GetBytes(text),
            null,
            CancellationToken.None
        );
        return result is null ? null : Encoding.UTF8.GetString(result);
    }
}
