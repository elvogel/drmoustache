namespace DrMoustache.Core.Delivery;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DrMoustache.Core.Binding;
using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Helpers;
using DrMoustache.Core.Rendering;
using DrMoustache.Core.Template;

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
    public static DeliveryResult Plan(
        RenderConfig config,
        IReadOnlyList<TemplateNode> outputPattern,
        IReadOnlyList<TemplateNode> body,
        string passthrough,
        IReadOnlyList<object> records)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(records);

        return config.Delivery == DeliveryMode.Collate
            ? Collate(config, body, passthrough, records)
            : Fanout(config, outputPattern, body, passthrough, records);
    }

    private static DeliveryResult Collate(
        RenderConfig config, IReadOnlyList<TemplateNode> body,
        string passthrough, IReadOnlyList<object> records)
    {
        var root = new CollateScope(records.Count, records);
        var content = Compose(body, passthrough, root);

        return new DeliveryResult([new RenderedDocument(config.Output, content)], []);
    }

    private static DeliveryResult Fanout(
        RenderConfig config,
        IReadOnlyList<TemplateNode> outputPattern,
        IReadOnlyList<TemplateNode> body,
        string passthrough,
        IReadOnlyList<object> records)
    {
        var policy = config.OnCollision
            ?? throw new DeliveryException(
                "'onCollision' is required when delivery is 'fanout'");

        var byPath = new Dictionary<string, List<(object Record, string Content)>>(
            StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var record in records)
        {
            var path = RenderOutputPath(outputPattern, record);

            if (!byPath.TryGetValue(path, out var bucket))
            {
                bucket = [];
                byPath[path] = bucket;
                order.Add(path);
            }

            bucket.Add((record, Compose(body, passthrough, record)));
        }

        var documents = new List<RenderedDocument>();
        var warnings = new List<string>();

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
                            i == 0 ? path : Insert(path, $"-{i + 1}"), bucket[i].Content));

                    warnings.Add(
                        $"collision on '{path}': {bucket.Count} records, "
                        + "numbered by arrival order (onCollision: ordinal)");
                    break;

                case CollisionPolicy.Suffix:
                    foreach (var (record, content) in bucket)
                        documents.Add(new RenderedDocument(
                            Insert(path, "." + ShortHash(record)), content));

                    warnings.Add(
                        $"collision on '{path}': {bucket.Count} records, "
                        + "disambiguated by record hash (onCollision: suffix)");
                    break;

                default:
                    throw new DeliveryException($"unhandled collision policy '{policy}'");
            }
        }

        return new DeliveryResult(documents, warnings);
    }

    private static string Compose(
        IReadOnlyList<TemplateNode> body, string passthrough, object? scope)
    {
        var content = TemplateRenderer.Render(body, scope);

        if (string.IsNullOrWhiteSpace(passthrough)) return content;

        var frontmatter = TemplateRenderer.Render(
            TemplateLexer.Lex(passthrough), scope);

        return $"---\n{frontmatter.TrimEnd('\n')}\n---\n{content}";
    }

    /// <summary>Renders an 'output' pattern into a path, slugifying each
    /// interpolated value individually before it is spliced in. Directory
    /// structure comes only from literal '/' the template author wrote —
    /// never from a record's own data. This is a security boundary, not a
    /// formatting preference: rendering the pattern as ordinary template
    /// text and then slugifying the *result* (the previous approach) let a
    /// field value containing '/' invent directory structure, and one
    /// containing '../' escape the output directory entirely.</summary>
    private static string RenderOutputPath(IReadOnlyList<TemplateNode> pattern, object? record)
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
                    sb.Append(Slug.Make(ValueText.Format(
                        Lookup(record, interp.Path), CultureInfo.InvariantCulture)));
                    break;

                case RawNode raw:
                    // A raw tag suppresses Markdown escaping, not path safety.
                    sb.Append(Slug.Make(ValueText.Format(
                        Lookup(record, raw.Path), CultureInfo.InvariantCulture)));
                    break;

                case HelperNode helper:
                    sb.Append(Slug.Make(HelperRegistry.Apply(
                        helper.Helper, Lookup(record, helper.Path), helper.Argument,
                        helper.Line, CultureInfo.InvariantCulture)));
                    break;

                default:
                    throw new DeliveryException(
                        $"'{node.GetType().Name}' is not supported in an 'output' pattern");
            }
        }

        return SanitizePath(sb.ToString());
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

    /// <summary>Six hex characters of SHA-256 over the record's string form.
    /// Deterministic across runs, so filenames do not churn when the provider
    /// changes its ordering.</summary>
    private static string ShortHash(object record)
    {
        var text = record.ToString() ?? "";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return string.Concat(bytes.Take(3)
            .Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>Root scope for collate delivery.</summary>
    private sealed record CollateScope(int Count, IReadOnlyList<object> Records);
}
