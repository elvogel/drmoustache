namespace DrMoustache.Rendering;

using DrMoustache.Abstractions;
using DrMoustache.Core.Delivery;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Template;
using DrMoustache.Core.Validation;
using DrMoustache.Discovery;
using DrMoustache.Output;
using DrMoustache.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal sealed class DocumentRenderer(
    IServiceProvider services,
    IEnumerable<SourceRegistration> registrations,
    IOptions<DrMoustacheOptions> options) : IDocumentRenderer
{
    private readonly List<SourceRegistration> _registrations = [.. registrations];
    private readonly DrMoustacheOptions _options = options.Value;

    public async Task<RenderResult> RenderAsync(CancellationToken ct = default)
    {
        var templates = TemplateFile.Discover(_options.TemplateDirectory);

        var problems = Validate(templates);

        if (problems.Count > 0)
            throw new RenderException(string.Join(Environment.NewLine, problems));

        var writer = new DocumentWriter(_options.OutputDirectory);

        var written = 0;
        var warnings = new List<string>();
        var skips = new List<RecordSkip>();

        foreach (var template in templates)
        {
            var config = template.Parsed.Config;
            var provider = Resolve(config.Source);
            var manifest = ManifestBuilder.FromType(provider.RecordType);

            var (records, templateSkips) = await DrainAsync(provider, manifest, ct)
                .ConfigureAwait(false);

            skips.AddRange(templateSkips);

            DeliveryResult result;

            try
            {
                result = DeliveryPlanner.Plan(
                    config,
                    TemplateLexer.Lex(config.Output),
                    TemplateLexer.Lex(template.Parsed.Body),
                    template.Parsed.PassthroughFrontmatter,
                    records,
                    _options.Culture);
            }
            catch (Exception ex) when (ex is TemplateException or DeliveryException)
            {
                throw new RenderException($"{template.Path}: {ex.Message}", ex);
            }

            warnings.AddRange(result.Warnings);
            written += await writer.WriteAllAsync(result.Documents, ct).ConfigureAwait(false);
        }

        var success = !(_options.Strict && skips.Count > 0);
        return new RenderResult(written, warnings, skips, success);
    }

    public Task<CheckResult> CheckAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var templates = TemplateFile.Discover(_options.TemplateDirectory);

        return Task.FromResult(new CheckResult(templates.Count, Validate(templates)));
    }

    private List<string> Validate(IReadOnlyList<TemplateFile> templates)
    {
        var problems = new List<string>();

        foreach (var template in templates)
        {
            var config = template.Parsed.Config;
            var provider = Resolve(config.Source);
            var manifest = ManifestBuilder.FromType(provider.RecordType);

            // The delivery mode is load-bearing: a collate template validated
            // as fanout reports 'count' and 'records' as unknown fields and
            // then never descends into the loop body, so it fails valid
            // templates and passes broken ones.
            var found = TemplateValidator.Validate(
                TemplateLexer.Lex(template.Parsed.Body), manifest, config.Delivery);

            foreach (var problem in found)
                problems.Add($"{template.Path}: {problem}");
        }

        return problems;
    }

    private IDataProvider Resolve(string source)
    {
        var provider = services.GetKeyedService<IDataProvider>(source);

        if (provider is not null) return provider;

        var available = _registrations.Count == 0
            ? "none are registered"
            : string.Join(", ", _registrations.Select(r => r.Name).Order(StringComparer.Ordinal));

        throw new RenderException(
            $"no source named '{source}' is registered. Available: {available}. "
            + "Register it with .AddSource<YourProvider>(\"" + source + "\").");
    }

    /// <summary>Materializes a provider's records, validating each.</summary>
    /// <remarks>Delivery needs every record before it can name any output file,
    /// because a collision is a property of the whole set. The provider contract
    /// stays asynchronous so it can stream and be cancelled; buffering stops here.</remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1031:Do not catch general exception types",
        Justification = "The provider is caller-supplied and may throw anything; a fault "
            + "must end that source's read without losing what was already drained, so it "
            + "is reported as a RecordSkip rather than propagated.")]
    private static async Task<(List<object> Records, List<RecordSkip> Skips)> DrainAsync(
        IDataProvider provider, RecordManifest manifest, CancellationToken ct)
    {
        var records = new List<object>();
        var skips = new List<RecordSkip>();
        var ordinal = 0;

        var enumerator = provider.ReadAsync(ct)
            .WithCancellation(ct)
            .ConfigureAwait(false)
            .GetAsyncEnumerator();

        try
        {
            while (true)
            {
                object record;

                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    record = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // An iterator cannot be resumed after it throws, so this
                    // ends the read. Everything already drained still renders.
                    skips.Add(new RecordSkip(provider.Name, ordinal,
                        $"provider threw: {ex.Message}"));
                    break;
                }

                if (RecordValidator.Validate(record, manifest) is { } reason)
                    skips.Add(new RecordSkip(provider.Name, ordinal, reason));
                else
                    records.Add(record);

                ordinal++;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        return (records, skips);
    }
}
