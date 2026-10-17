using System;
using System.Linq;
using System.Text;
using System.Threading;
using CSharpier.Core;
using Dprint.Plugins.CSharpier.Configuration;

namespace Dprint.Plugins.CSharpier.Formatters;

/// <summary>
/// Formats files with CSharpier.
/// </summary>
public class FileFormatter
{
    public FileFormatter(ResolvedConfiguration config)
    {
        Config = config;
    }

    public ResolvedConfiguration Config { get; }

    /// <summary>
    /// Formats the file returning the formatted bytes or null when there was no change.
    /// </summary>
    public byte[]? Format(
        string filePath,
        byte[] code,
        (int Start, int End)? byteRange,
        CancellationToken token
    )
    {
        var formatter = PrinterOptions.GetFormatter(filePath);
        if (formatter == Formatter.Unknown)
            throw new Exception($"Could not find formatter for file path: {filePath}");
        // CSharpier can only format a whole file
        if (byteRange is { } range && (range.Start > 0 || range.End < code.Length))
            return null;
        // the CSharpier CLI skips these files too
        if (!Config.IncludeGenerated && GeneratedCodeUtilities.IsGeneratedCodeFile(filePath))
            return null;

        var encoding = DetectEncoding(code);
        var preambleLength = encoding.Preamble.Length;
        var text = encoding.GetString(code, preambleLength, code.Length - preambleLength);
        var result = CodeFormatter
            .FormatAsync(text, CreatePrinterOptions(formatter, filePath, text), token)
            .GetAwaiter()
            .GetResult();
        if (result.ErrorDiagnostics.Any())
            throw new Exception(
                $"Was not formatted due to syntax errors.\n{string.Join("\n", result.ErrorDiagnostics)}"
            );
        // CSharpier returns empty code in some of these cases
        if (!string.IsNullOrEmpty(result.FailureMessage))
            throw new Exception(result.FailureMessage);
        if (!string.IsNullOrEmpty(result.WarningMessage))
            throw new Exception(result.WarningMessage);

        var bytes = GetBytes(result.Code, encoding);
        return bytes.AsSpan().SequenceEqual(code) ? null : bytes;
    }

    private PrinterOptions CreatePrinterOptions(Formatter formatter, string filePath, string text)
    {
        var languageConfig = formatter == Formatter.XML ? Config.Xml : Config.CSharp;
        var xmlWhitespaceSensitivity =
            Config.XmlWhitespaceSensitivity ?? PrinterOptions.GetXmlWhitespaceSensitivity(filePath);
        return new PrinterOptions(formatter, xmlWhitespaceSensitivity)
        {
            Width = languageConfig.LineWidth,
            IndentSize = languageConfig.IndentWidth,
            UseTabs = languageConfig.UseTabs,
            // resolve "auto" here because CSharpier uses the first line ending instead of the last like dprint
            EndOfLine = languageConfig.GetNewLine(text) == "\r\n" ? EndOfLine.CRLF : EndOfLine.LF,
            IncludeGenerated = Config.IncludeGenerated,
        };
    }

    private static byte[] GetBytes(string text, Encoding encoding)
    {
        var preamble = encoding.Preamble;
        var bytes = new byte[preamble.Length + encoding.GetByteCount(text)];
        preamble.CopyTo(bytes);
        encoding.GetBytes(text, 0, text.Length, bytes, preamble.Length);
        return bytes;
    }

    /// <summary>
    /// Gets the encoding based on the byte order mark, defaulting to UTF-8.
    /// </summary>
    private static Encoding DetectEncoding(byte[] code)
    {
        Encoding[] encodingsWithPreamble =
        [
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
            new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true),
            new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true),
        ];
        return encodingsWithPreamble.FirstOrDefault(encoding =>
                code.AsSpan().StartsWith(encoding.Preamble)
            )
            ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }
}
