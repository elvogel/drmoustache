namespace DrMoustache.Core.Rendering;

/// <param name="Path">Relative output path, assigned by the delivery planner.</param>
/// <param name="Content">The fully rendered document body.</param>
/// <remarks>Documents are produced whole, in memory. Nothing reaches disk until
/// a document is complete, so a mid-render failure leaves no partial file.</remarks>
public sealed record RenderedDocument(string Path, string Content);
