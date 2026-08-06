namespace DrMoustache.Core.Delivery;

using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DrMoustache.Core.Binding;
using DrMoustache.Core.Escaping;
using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Helpers;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Rendering;
using DrMoustache.Core.Template;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

public sealed record DeliveryResult(
    IReadOnlyList<RenderedDocument> Documents,
    IReadOnlyList<string> Warnings);

/// <summary>A fault while distributing records across output paths.</summary>
public sealed class DeliveryException : Exception
{
    public DeliveryException() { }

    public DeliveryException(string message) : base(message) { }

    public DeliveryException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Distributes records across output documents.</summary>
public static class DeliveryPlanner
{
    /// <summary>Separates field values inside a record's hash pre-image so
    /// two different field splits cannot produce the same byte sequence.</summary>
    private const char HashSeparator = '\u001f';

    private static readonly IDeserializer YamlReader = new DeserializerBuilder().Build();

    private static readonly ISerializer YamlWriter = new SerializerBuilder().Build();

    /// <param name="config">The template's rendering configuration.</param>
    /// <param name="outputPattern">The lexed 'output' pattern.</param>
    /// <param name="body">The lexed template body.</param>
    /// <param name="passthrough">The frontmatter block to render per document.</param>
    /// <param name="records">The records to plan documents for.</param>
    /// <param name="culture">Formatting culture. Null means invariant, so
    /// output is byte-identical across machines.</param>
    public static DeliveryResult Plan(
        RenderConfig config,
        IReadOnlyList<TemplateNode> outputPattern,
        IReadOnlyList<TemplateNode> body,
        string passthrough,
        IReadOnlyList<object> records,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(records);

        return config.Delivery == DeliveryMode.Collate
            ? Collate(config, body, passthrough, records, culture)
            : Fanout(config, outputPattern, body, passthrough, records, culture);
    }

    private static DeliveryResult Collate(
        RenderConfig config, IReadOnlyList<TemplateNode> body,
        string passthrough, IReadOnlyList<object> records, CultureInfo? culture)
    {
        var root = new CollateScope(records.Count, records);
        var content = Compose(body, passthrough, root, culture);

        return new DeliveryResult([new RenderedDocument(config.Output, content)], []);
    }

    private static DeliveryResult Fanout(
        RenderConfig config,
        IReadOnlyList<TemplateNode> outputPattern,
        IReadOnlyList<TemplateNode> body,
        string passthrough,
        IReadOnlyList<object> records,
        CultureInfo? culture)
    {
        var policy = config.OnCollision
            ?? throw new DeliveryException(
                "'onCollision' is required when delivery is 'fanout'");

        var byPath = new Dictionary<string, List<(object Record, string Content)>>(
            StringComparer.Ordinal);
        var order = new List<string>();
        var warnings = new List<string>();

        foreach (var record in records)
        {
            var path = RenderOutputPath(outputPattern, record, warnings);

            if (!byPath.TryGetValue(path, out var bucket))
            {
                bucket = [];
                byPath[path] = bucket;
                order.Add(path);
            }

            bucket.Add((record, Compose(body, passthrough, record, culture)));
        }

        var documents = new List<RenderedDocument>();

        // Every base path is claimed up front, so a generated '-2' or '.9c3f1a'
        // suffix can never land on a path some other record legitimately owns.
        // 'a', 'a', 'a-2' used to produce a.md, a-2.md, a-2.md — two documents
        // at one path, the second silently overwriting the first on write.
        var claimed = new HashSet<string>(order, StringComparer.Ordinal);

        // The manifest is per record type, and every record in a plan shares
        // one, so it is built at most once here rather than once per record.
        RecordManifest? manifest = null;

        foreach (var path in order)
        {
            var bucket = byPath[path];

            if (bucket.Count == 1)
            {
                documents.Add(new RenderedDocument(path, bucket[0].Content));
                continue;
            }

            switch (policy)
            {
                case CollisionPolicy.Error:
                    throw new DeliveryException(
                        $"output collision: '{path}' produced by "
                        + $"{bucket.Count} records. Add a more distinguishing field "
                        + "to 'output', or set 'onCollision'.");

                case CollisionPolicy.Overwrite:
                    documents.Add(new RenderedDocument(path, bucket[^1].Content));
                    warnings.Add(
                        $"collision on '{path}': {bucket.Count} records, "
                        + "kept the last (onCollision: overwrite)");
                    break;

                case CollisionPolicy.Ordinal:
                    for (var i = 0; i < bucket.Count; i++)
                        documents.Add(new RenderedDocument(
                            i == 0 ? path : ClaimOrdinal(path, claimed), bucket[i].Content));

                    warnings.Add(
                        $"collision on '{path}': {bucket.Count} records, "
                        + "numbered by arrival order (onCollision: ordinal)");
                    break;

                case CollisionPolicy.Suffix:
                    manifest ??= ManifestFor(bucket[0].Record);

                    foreach (var (record, content) in bucket)
                    {
                        var hashed = Insert(path, "." + ShortHash(record, manifest));

                        // Two records whose every declared field value is equal
                        // hash alike, and a hashed name can in principle equal
                        // a path some other record already owns. Either way the
                        // record still gets a home: dropping one is never
                        // acceptable.
                        if (!claimed.Add(hashed))
                        {
                            var taken = hashed;
                            hashed = ClaimOrdinal(hashed, claimed);
                            warnings.Add(
                                $"collision on '{path}': '{taken}' was already claimed, "
                                + $"so a record was written to '{hashed}' instead");
                        }

                        documents.Add(new RenderedDocument(hashed, content));
                    }

                    warnings.Add(
                        $"collision on '{path}': {bucket.Count} records, "
                        + "disambiguated by record hash (onCollision: suffix)");
                    break;

                default:
                    throw new DeliveryException($"unhandled collision policy '{policy}'");
            }
        }

        AssertDistinct(documents);

        return new DeliveryResult(documents, warnings);
    }

    /// <summary>Silently writing two documents to one path is the single
    /// outcome the design says is never acceptable, so the planner refuses to
    /// hand one to a writer. Every branch above already guarantees this; the
    /// assertion exists so a future branch that does not cannot ship.</summary>
    private static void AssertDistinct(IReadOnlyList<RenderedDocument> documents)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in documents)
            if (!seen.Add(document.Path))
                throw new DeliveryException(
                    $"internal error: two documents were planned for '{document.Path}'. "
                    + "This would silently discard a record and is refused.");
    }

    /// <summary>Appends '-2', '-3', … until the result is a path no other
    /// document has claimed, then claims it.</summary>
    private static string ClaimOrdinal(string path, HashSet<string> claimed)
    {
        for (var n = 2; ; n++)
        {
            var candidate = Insert(path, $"-{n}");
            if (claimed.Add(candidate)) return candidate;
        }
    }

    private static string Compose(
        IReadOnlyList<TemplateNode> body, string passthrough, object? scope,
        CultureInfo? culture)
    {
        var content = TemplateRenderer.Render(body, scope, culture);

        if (string.IsNullOrWhiteSpace(passthrough)) return content;

        var frontmatter = RenderFrontmatter(passthrough, scope, culture).TrimEnd('\n');

        return frontmatter.Length == 0 ? content : $"---\n{frontmatter}\n---\n{content}";
    }

    /// <summary>Renders the passthrough frontmatter block.
    ///
    /// The block is YAML, not Markdown, so it must not go through
    /// <see cref="MarkdownEscaper"/>: a value of <c>Acme *Corp*</c> would be
    /// emitted as <c>Acme \*Corp\*</c>, which is not a legal escape in a
    /// double-quoted YAML scalar; <c>C:\temp</c> would be emitted verbatim and
    /// silently read back as <c>C:&lt;TAB&gt;emp</c>; and a value containing a
    /// quote or a newline would end the scalar early. So the block is parsed as
    /// YAML, each scalar is rendered as a template with escaping off, and the
    /// result is re-serialised — letting YamlDotNet decide the quoting, which
    /// is the only component here that knows the YAML rules.
    ///
    /// One consequence worth stating: the emitted frontmatter is YamlDotNet's
    /// formatting of the author's structure, not the author's own bytes, so
    /// comments and hand-chosen quoting styles do not survive. That is the
    /// price of guaranteeing the output parses.</summary>
    private static string RenderFrontmatter(
        string passthrough, object? scope, CultureInfo? culture)
    {
        GuardUnquotedTag(passthrough);

        object? graph;

        try
        {
            graph = YamlReader.Deserialize<object?>(passthrough);
        }
        catch (YamlException ex)
        {
            throw new DeliveryException(
                $"the template's frontmatter is not valid YAML: {ex.Message}", ex);
        }

        if (graph is null) return "";

        try
        {
            return YamlWriter.Serialize(RenderYamlNode(graph, scope, culture));
        }
        catch (YamlException ex)
        {
            throw new DeliveryException(
                $"the rendered frontmatter could not be written as YAML: {ex.Message}", ex);
        }
    }

    /// <summary>Rejects a frontmatter value written as a bare <c>{{tag}}</c>.
    ///
    /// Parsing the block as YAML *before* interpolation is what makes the
    /// emitted frontmatter safe, but it also means the block must already be
    /// valid YAML — and YAML reads a leading <c>{</c> as a flow mapping, so
    /// <c>title: {{name}}</c> parses as a mapping whose key is a mapping and
    /// emits <c>title:\n  ? name:\n  :</c>, losing the value entirely and
    /// without a word. The design says this form *fails*, and names the error
    /// it should produce; silently emitting a garbage complex key is not
    /// failing.
    ///
    /// <c>FrontmatterParser.GuardLeadingBrace</c> raises this same message when
    /// a template is parsed, which is the path a CLI render takes. This guard
    /// covers the planner being called directly — by an embedded host, or by a
    /// test — so the mistake produces one consistent message either way.</summary>
    private static void GuardUnquotedTag(string passthrough)
    {
        foreach (var line in passthrough.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0) continue;

            var value = line[(colon + 1)..].Trim();
            if (!value.StartsWith("{{", StringComparison.Ordinal)) continue;

            var rawKey = line[..colon];
            var indent = rawKey[..(rawKey.Length - rawKey.TrimStart().Length)];
            var key = rawKey.Trim();

            throw new DeliveryException(
                "value must be quoted\n"
                + $"       {indent}{key}: {value}\n"
                + $"       {indent}{new string(' ', key.Length + 2)}^ YAML reads a leading "
                + "'{' as a flow mapping\n"
                + $"       write:  {indent}{key}: \"{value}\"");
        }
    }

    /// <summary>The same mistake in a position the line scan cannot see, such as
    /// <c>tags: [a, {{name}}]</c>: YAML turned the tag into a mapping, so it
    /// arrives here as a non-scalar mapping key. Nothing legitimate produces
    /// one, and letting it through would emit the same silent corruption.</summary>
    private static DeliveryException UnquotedTag(object key) =>
        new("value must be quoted\n"
            + $"       a frontmatter value of {DescribeTag(key)} is read by YAML as a "
            + "flow mapping, not as text\n"
            + $"       write:  \"{DescribeTag(key)}\"");

    private static string DescribeTag(object key) =>
        key is IDictionary { Count: 1 } inner
        && inner.Keys.Cast<object>().First() is string name
            ? $"{{{{{name}}}}}"
            : "{{…}}";

    // Matched through the non-generic interfaces on purpose: YamlDotNet hands
    // back Dictionary<object, object> and List<object>, and generic interfaces
    // are invariant, so IDictionary<object, object?> would never match.
    private static object? RenderYamlNode(object? node, object? scope, CultureInfo? culture)
        => node switch
        {
            string text => RenderUnescaped(text, scope, culture),
            IDictionary map => RenderMap(map, scope, culture),
            IList list => RenderList(list, scope, culture),
            _ => node,
        };

    private static Dictionary<object, object?> RenderMap(
        IDictionary map, object? scope, CultureInfo? culture)
    {
        var result = new Dictionary<object, object?>();

        foreach (DictionaryEntry entry in map)
        {
            if (entry.Key is IDictionary or IList) throw UnquotedTag(entry.Key);

            result[RenderYamlNode(entry.Key, scope, culture) ?? ""] =
                RenderYamlNode(entry.Value, scope, culture);
        }

        return result;
    }

    private static List<object?> RenderList(IList list, object? scope, CultureInfo? culture)
    {
        var result = new List<object?>(list.Count);

        foreach (var item in list) result.Add(RenderYamlNode(item, scope, culture));

        return result;
    }

    /// <summary>Renders one YAML scalar as a Mustache template with Markdown
    /// escaping switched off. <see cref="MarkdownContext.FencedCode"/> is the
    /// context that escapes nothing, so rewriting each tag to it is how the
    /// ordinary renderer — scope walking, helpers, sections and all — is reused
    /// without any of its Markdown behaviour.</summary>
    private static string RenderUnescaped(string text, object? scope, CultureInfo? culture)
        => text.Contains("{{", StringComparison.Ordinal)
            ? TemplateRenderer.Render(WithoutEscaping(TemplateLexer.Lex(text)), scope, culture)
            : text;

    private static IReadOnlyList<TemplateNode> WithoutEscaping(
        IReadOnlyList<TemplateNode> nodes) =>
        [.. nodes.Select(node => node switch
        {
            InterpolationNode i =>
                (TemplateNode)new InterpolationNode(i.Path, MarkdownContext.FencedCode, i.Line),
            HelperNode h => new HelperNode(
                h.Helper, h.Path, h.Argument, MarkdownContext.FencedCode, h.Line),
            SectionNode s => new SectionNode(
                s.Path, s.Inverted, WithoutEscaping(s.Children), s.Line),
            _ => node,
        })];

    /// <summary>Renders an 'output' pattern into a path, slugifying each
    /// interpolated value individually before it is spliced in. Directory
    /// structure comes only from literal '/' the template author wrote —
    /// never from a record's own data. This is a security boundary, not a
    /// formatting preference: rendering the pattern as ordinary template
    /// text and then slugifying the *result* (the previous approach) let a
    /// field value containing '/' invent directory structure, and one
    /// containing '../' escape the output directory entirely.</summary>
    private static string RenderOutputPath(
        IReadOnlyList<TemplateNode> pattern, object? record, List<string> warnings)
    {
        var sb = new StringBuilder();

        foreach (var node in pattern)
        {
            switch (node)
            {
                case TextNode text:
                    // The template author's own literal separators — the
                    // only legitimate source of directory structure.
                    sb.Append(text.Text);
                    break;

                case InterpolationNode interp:
                    AppendSlug(sb, ValueText.Format(
                        Lookup(record, interp.Path), CultureInfo.InvariantCulture),
                        interp.Path, warnings);
                    break;

                case RawNode raw:
                    // A raw tag suppresses Markdown escaping, not path safety.
                    AppendSlug(sb, ValueText.Format(
                        Lookup(record, raw.Path), CultureInfo.InvariantCulture),
                        raw.Path, warnings);
                    break;

                case HelperNode helper:
                    AppendSlug(sb, HelperRegistry.Apply(
                        helper.Helper, Lookup(record, helper.Path), helper.Argument,
                        helper.Line, CultureInfo.InvariantCulture),
                        helper.Path, warnings);
                    break;

                default:
                    throw new DeliveryException(
                        $"'{node.GetType().Name}' is not supported in an 'output' pattern");
            }
        }

        return SanitizePath(sb.ToString());
    }

    /// <summary>The placeholder standing in for a value that slugs away to
    /// nothing.</summary>
    private const string EmptySegmentPlaceholder = "untitled";

    /// <summary>Appends one interpolated value's slug. An empty slug is
    /// replaced rather than dropped: 'out/{{a}}/{{b}}.md' with both values
    /// absent used to assemble 'out//.md', which the path sanitiser reduced to
    /// 'out/.md' — a hidden file with no name, produced silently, when
    /// <see cref="Slug"/> strips leading dots precisely to avoid one. The
    /// substitution is warned about and names the field, because a document
    /// whose filename came from nowhere is something the operator needs to
    /// know.</summary>
    private static void AppendSlug(
        StringBuilder sb, string value, string field, List<string> warnings)
    {
        var slug = Slug.Make(value);

        if (slug.Length > 0)
        {
            sb.Append(slug);
            return;
        }

        sb.Append(EmptySegmentPlaceholder);

        var warning =
            $"field '{field}' is empty or slugs to nothing in the 'output' pattern; "
            + $"used '{EmptySegmentPlaceholder}' for that path segment";

        // One warning per field, not one per record: a provider yielding 5,000
        // records with a blank field must not emit 5,000 identical lines.
        if (!warnings.Contains(warning, StringComparer.Ordinal)) warnings.Add(warning);
    }

    private static object? Lookup(object? record, string path) =>
        ValueResolver.TryResolve(record, path, out var value) ? value : null;

    /// <summary>Belt-and-braces guard on the fully assembled path. Every
    /// value segment was already slugified in <see cref="RenderOutputPath"/>
    /// and can no longer contain '/', so this should be unreachable — but a
    /// security boundary warrants a second, independent check, and it
    /// documents the invariant for the next reader. Drops any segment that
    /// is empty, '.', or '..', and rejects an absolute path.</summary>
    private static string SanitizePath(string path)
    {
        if (Path.IsPathRooted(path))
            throw new DeliveryException(
                $"'output' pattern produced an absolute path: '{path}'");

        var segments = path.Split('/')
            .Where(s => s.Length > 0 && s != "." && s != "..")
            .ToArray();

        if (segments.Length == 0)
            throw new DeliveryException(
                $"'output' pattern produced an empty path: '{path}'");

        return string.Join('/', segments);
    }

    private static string Insert(string path, string suffix)
    {
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');

        return dot > slash && dot >= 0
            ? string.Concat(path.AsSpan(0, dot), suffix, path.AsSpan(dot))
            : path + suffix;
    }

    private static RecordManifest ManifestFor(object record)
    {
        try
        {
            return ManifestBuilder.FromType(record.GetType());
        }
        catch (ManifestException ex)
        {
            throw new DeliveryException(
                "'onCollision: suffix' hashes a record's declared field values, so the "
                + $"record type must declare them: {ex.Message}", ex);
        }
    }

    /// <summary>Six hex characters of SHA-256 over the record's declared field
    /// values, in manifest order, formatted invariantly.
    ///
    /// Not <c>record.ToString()</c>, which was wrong three ways and silently:
    /// a provider using a plain class rather than a record contributes only its
    /// type name, so every colliding record hashed alike and one of them
    /// vanished; two records differing only in a collection field hashed alike
    /// too, because the compiler-synthesised ToString prints
    /// 'Tags = System.String[]'; and ToString formats with the ambient culture,
    /// so the same records produced different filenames on different
    /// machines — against both the 'deterministic across runs' promise and the
    /// invariant-output principle.</summary>
    private static string ShortHash(object record, RecordManifest manifest)
    {
        var sb = new StringBuilder();
        AppendFields(sb, record, manifest.Fields);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));

        return string.Concat(bytes.Take(3)
            .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static void AppendFields(
        StringBuilder sb, object? scope, IReadOnlyList<ManifestField> fields)
    {
        foreach (var field in fields)
        {
            sb.Append(field.Path).Append('=');
            AppendValue(sb, Lookup(scope, field.Path), field);
            sb.Append(HashSeparator);
        }
    }

    private static void AppendValue(StringBuilder sb, object? value, ManifestField field)
    {
        if (value is null)
        {
            // Distinguishable from an empty string, which formats to nothing.
            sb.Append("\u0000null");
            return;
        }

        switch (field.Type)
        {
            case FieldType.Object:
                sb.Append('{');
                AppendFields(sb, value, field.Children);
                sb.Append('}');
                break;

            case FieldType.Collection when ValueResolver.AsSequence(value) is { } items:
                sb.Append('[');

                foreach (var item in items)
                {
                    if (field.Children.Count > 0) AppendFields(sb, item, field.Children);
                    else sb.Append(ValueText.Format(item, CultureInfo.InvariantCulture));

                    sb.Append(HashSeparator);
                }

                sb.Append(']');
                break;

            default:
                sb.Append(ValueText.Format(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>Root scope for collate delivery.</summary>
    private sealed record CollateScope(int Count, IReadOnlyList<object> Records);
}
