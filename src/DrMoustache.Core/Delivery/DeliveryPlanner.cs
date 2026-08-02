namespace DrMoustache.Core.Delivery;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DrMoustache.Core.Frontmatter;
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
            var path = SlugifyPath(TemplateRenderer.Render(outputPattern, record));

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

    private static string SlugifyPath(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Slug.Make));

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
