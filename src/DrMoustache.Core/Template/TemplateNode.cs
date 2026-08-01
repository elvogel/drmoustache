namespace DrMoustache.Core.Template;

using DrMoustache.Core.Escaping;

public abstract record TemplateNode;

public sealed record TextNode(string Text) : TemplateNode;

/// <param name="Path">The dotted lookup path, e.g. <c>order.total</c>.</param>
/// <param name="Context">Where this tag sits in the Markdown, captured at lex
/// time so the renderer never re-derives it.</param>
/// <param name="Line">One-based source line the tag started on.</param>
public sealed record InterpolationNode(string Path, MarkdownContext Context, int Line)
    : TemplateNode;

/// <summary>A triple-brace tag. Never escaped.</summary>
public sealed record RawNode(string Path, int Line) : TemplateNode;

public sealed record HelperNode(
    string Helper, string Path, string? Argument, MarkdownContext Context, int Line)
    : TemplateNode;

public sealed record SectionNode(
    string Path, bool Inverted, IReadOnlyList<TemplateNode> Children, int Line)
    : TemplateNode;
