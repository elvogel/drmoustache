namespace DrMoustache.Rendering;

/// <summary>Renders every discovered template against its registered source.</summary>
public interface IDocumentRenderer
{
    /// <summary>Validates, reads, renders, and writes.</summary>
    /// <remarks>Validation runs first: template faults are static and affect
    /// every record, so they fail before any provider is touched.</remarks>
    Task<RenderResult> RenderAsync(CancellationToken ct = default);

    /// <summary>Validates every template against its source's manifest without
    /// reading any data or writing any output.</summary>
    Task<CheckResult> CheckAsync(CancellationToken ct = default);
}
