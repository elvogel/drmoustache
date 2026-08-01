namespace DrMoustache.Core.Frontmatter;

using System.Text;
using DrMoustache.Core.Template;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>Splits a template into its reserved configuration block, its
/// passthrough frontmatter, and its body.</summary>
public static class FrontmatterParser
{
    private const string ReservedKey = "drmoustache";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ParsedTemplate Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var (frontmatter, body, frontmatterStartLine) = SplitFrontmatter(content);
        var (reserved, passthrough, keyLines, headerLine) =
            SplitReservedBlock(frontmatter, frontmatterStartLine);

        return new ParsedTemplate(ReadConfig(reserved, keyLines, headerLine), passthrough, body);
    }

    private static (string Frontmatter, string Body, int StartLine) SplitFrontmatter(
        string content)
    {
        var normalised = content.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!normalised.StartsWith("---\n", StringComparison.Ordinal))
            throw new TemplateException(
                "template has no YAML frontmatter; it must begin with a '---' line", 1);

        var end = normalised.IndexOf("\n---", 3, StringComparison.Ordinal);

        while (end >= 0)
        {
            var afterMarker = end + 4;
            var restOfLine = normalised.AsSpan(afterMarker);
            var lineBreak = restOfLine.IndexOf('\n');
            var trailing = lineBreak < 0 ? restOfLine : restOfLine[..lineBreak];

            if (trailing.IsWhiteSpace()) break;

            end = normalised.IndexOf("\n---", afterMarker, StringComparison.Ordinal);
        }

        if (end < 0)
            throw new TemplateException(
                "frontmatter was not terminated; add a closing '---' line", 1);

        var frontmatter = normalised[4..end];
        var bodyStart = normalised.IndexOf('\n', end + 4);
        var body = bodyStart < 0 ? "" : normalised[(bodyStart + 1)..];

        return (frontmatter, body, StartLine: 2);
    }

    /// <summary>Separates the <c>drmoustache</c> block from every other key by
    /// indentation, so the passthrough half survives as raw text. Also records
    /// the line each reserved key was found on, and the line of the
    /// <c>drmoustache:</c> header itself as a fallback for errors about a key
    /// that is missing entirely.</summary>
    private static (
        string Reserved,
        string Passthrough,
        IReadOnlyDictionary<string, int> KeyLines,
        int HeaderLine) SplitReservedBlock(string frontmatter, int startLine)
    {
        var lines = frontmatter.Split('\n');
        var reserved = new StringBuilder();
        var passthrough = new StringBuilder();
        var keyLines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var inReserved = false;
        var headerLine = startLine;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = startLine + i;

            if (line.StartsWith(ReservedKey + ":", StringComparison.Ordinal))
            {
                inReserved = true;
                headerLine = lineNumber;
                continue;
            }

            var isIndented = line.Length > 0 && char.IsWhiteSpace(line[0]);

            if (inReserved && (isIndented || line.Trim().Length == 0))
            {
                GuardLeadingBrace(line, lineNumber);

                var trimmed = line.TrimStart();
                var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0) keyLines[trimmed[..colon].Trim()] = lineNumber;

                reserved.Append(trimmed).Append('\n');
                continue;
            }

            inReserved = false;
            GuardLeadingBrace(line, lineNumber);
            passthrough.Append(line).Append('\n');
        }

        if (reserved.Length == 0)
            throw new TemplateException(
                $"template has no '{ReservedKey}:' block in its frontmatter", startLine);

        return (reserved.ToString(), passthrough.ToString().TrimEnd('\n'), keyLines, headerLine);
    }

    /// <summary>YAML reads a leading '{' as a flow mapping, so a value starting
    /// with '{{' fails to parse. Detect it and say what to write instead,
    /// preserving the line's original indentation so the correction can be
    /// pasted back in directly.</summary>
    private static void GuardLeadingBrace(string line, int lineNumber)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return;

        var value = line[(colon + 1)..].Trim();
        if (!value.StartsWith("{{", StringComparison.Ordinal)) return;

        var rawKey = line[..colon];
        var indent = rawKey[..(rawKey.Length - rawKey.TrimStart().Length)];
        var key = rawKey.Trim();

        throw new TemplateException(
            $"value must be quoted (line {lineNumber})\n"
            + $"       {indent}{key}: {value}\n"
            + $"       {indent}{new string(' ', key.Length + 2)}^ YAML reads a leading '{{' "
            + "as a flow mapping\n"
            + $"       write:  {indent}{key}: \"{value}\"",
            lineNumber);
    }

    private static RenderConfig ReadConfig(
        string reservedYaml, IReadOnlyDictionary<string, int> keyLines, int headerLine)
    {
        ReservedBlock block;

        try
        {
            block = Yaml.Deserialize<ReservedBlock>(reservedYaml) ?? new ReservedBlock();
        }
        catch (YamlException ex)
        {
            throw new TemplateException(
                $"could not read the '{ReservedKey}' block: {ex.Message}", headerLine);
        }

        if (string.IsNullOrWhiteSpace(block.Source))
            throw new TemplateException("'source' is required", headerLine);

        if (string.IsNullOrWhiteSpace(block.Output))
            throw new TemplateException("'output' is required", headerLine);

        var deliveryLine = keyLines.GetValueOrDefault("delivery", headerLine);
        var delivery = block.Delivery switch
        {
            null => throw new TemplateException("'delivery' is required", headerLine),
            var d when d.Equals("fanout", StringComparison.OrdinalIgnoreCase) => DeliveryMode.Fanout,
            var d when d.Equals("collate", StringComparison.OrdinalIgnoreCase) => DeliveryMode.Collate,
            var other => throw new TemplateException(
                $"unknown delivery '{other}'; choose one: fanout | collate", deliveryLine),
        };

        var collisionLine = keyLines.GetValueOrDefault("onCollision", headerLine);
        CollisionPolicy? collision = block.OnCollision switch
        {
            null => null,
            var c when c.Equals("error", StringComparison.OrdinalIgnoreCase) => CollisionPolicy.Error,
            var c when c.Equals("suffix", StringComparison.OrdinalIgnoreCase) => CollisionPolicy.Suffix,
            var c when c.Equals("ordinal", StringComparison.OrdinalIgnoreCase) => CollisionPolicy.Ordinal,
            var c when c.Equals("overwrite", StringComparison.OrdinalIgnoreCase) => CollisionPolicy.Overwrite,
            var other => throw new TemplateException(
                $"unknown onCollision '{other}'; "
                + "choose one: error | suffix | ordinal | overwrite", collisionLine),
        };

        if (delivery == DeliveryMode.Fanout && collision is null)
            throw new TemplateException(
                "'onCollision' is required when delivery is 'fanout'\n"
                + "       choose one: error | suffix | ordinal | overwrite", headerLine);

        return new RenderConfig(block.Source!, delivery, block.Output!, collision);
    }

    private sealed class ReservedBlock
    {
        public string? Source { get; set; }
        public string? Delivery { get; set; }
        public string? Output { get; set; }
        public string? OnCollision { get; set; }
    }
}
