namespace DrMoustache.Rendering;

using DrMoustache.Abstractions;
using DrMoustache.Core.Delivery;
using DrMoustache.Core.Frontmatter;
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
        var faults = new List<string>();

        // Grouped by source, so a source two templates share is read once.
        // Draining per template read it twice: double the network or database
        // cost, skips counted twice, and — for a provider whose sequence is not
        // stable across calls — two templates rendered from different data.
        foreach (var group in templates.GroupBy(
            t => t.Parsed.Config.Source, StringComparer.Ordinal))
        {
            var provider = Resolve(group.Key);
            var manifest = ManifestBuilder.FromType(provider.RecordType);

            var (records, sourceSkips, sourceFaults) =
                await DrainAsync(provider, manifest, ct).ConfigureAwait(false);

            skips.AddRange(sourceSkips);
            faults.AddRange(sourceFaults);

            foreach (var template in group)
            {
                var config = template.Parsed.Config;

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
        }

        // A fault means the read died and we do not know what we missed, so it
        // fails the run whatever Strict says. Strict governs record-level skips,
        // where we do know exactly what was lost.
        var success = faults.Count == 0 && !(_options.Strict && skips.Count > 0);
        return new RenderResult(written, warnings, skips, faults, success);
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

            // Reporting problems is what this type is for: an unclosed section
            // or a 'source:' naming nothing registered are the two commonest
            // authoring mistakes, and --check exists to name the file they are
            // in. Without this catch they escape as an exception carrying no
            // path, leaving the user to bisect a directory by hand.
            try
            {
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

                // The output pattern is a template too, and RenderAsync lexes it.
                // Checking only the body would let a malformed pattern pass check
                // and then fail the render — the false green check exists to
                // prevent. Lexing catches syntax for either delivery mode; field
                // references are only meaningful for fanout, since collate uses
                // the output verbatim as a single file name.
                var outputNodes = TemplateLexer.Lex(config.Output);

                if (config.Delivery == DeliveryMode.Fanout)
                {
                    var inOutput = TemplateValidator.Validate(
                        outputNodes, manifest, DeliveryMode.Fanout);

                    foreach (var problem in inOutput)
                        problems.Add($"{template.Path}: output: {problem}");
                }
            }
            catch (Exception ex)
                when (ex is TemplateException or RenderException or ManifestException)
            {
                problems.Add($"{template.Path}: {ex.Message}");
            }
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
            + "is reported as a Fault rather than propagated.")]
    private static async Task<(List<object> Records, List<RecordSkip> Skips,
        List<string> Faults)> DrainAsync(
        IDataProvider provider, RecordManifest manifest, CancellationToken ct)
    {
        var records = new List<object>();
        var skips = new List<RecordSkip>();
        var faults = new List<string>();
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
                    // ends the read. Everything already drained still renders,
                    // but this is a fault, not a skip: we do not know how many
                    // records we never saw. The type is named because
                    // 'connection refused' and 'invalid cast' need different
                    // responses and the message alone often distinguishes
                    // neither.
                    faults.Add(
                        $"{provider.Name}: read failed after {ordinal} record"
                        + $"{(ordinal == 1 ? "" : "s")}: "
                        + $"{ex.GetType().FullName}: {ex.Message}");
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

        return (records, skips, faults);
    }
}
