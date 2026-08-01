namespace DrMoustache.Core.Escaping;

/// <summary>Where in a Markdown document a template tag sits. Determines
/// which characters an interpolated value must escape.</summary>
public enum MarkdownContext
{
    /// <summary>Mid-line prose. Emphasis and link syntax are live.</summary>
    Inline,

    /// <summary>Columns 0-3 of a line. Block constructs are live here, so a
    /// value beginning '#' would become a heading.</summary>
    LineStart,

    /// <summary>Inside a table row. Pipes and newlines break the row.</summary>
    TableRow,

    /// <summary>Inside a backtick span. Content is literal.</summary>
    CodeSpan,

    /// <summary>Inside a fenced block. Content is literal.</summary>
    FencedCode,

    /// <summary>Inside the target portion of a link. URL rules apply.</summary>
    LinkTarget,
}
