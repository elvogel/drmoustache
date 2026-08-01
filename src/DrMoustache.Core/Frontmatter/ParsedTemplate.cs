namespace DrMoustache.Core.Frontmatter;

/// <param name="Config">The reserved render configuration.</param>
/// <param name="PassthroughFrontmatter">Raw YAML of every key except
/// <c>drmoustache</c>. Kept as text because it is itself a template,
/// interpolated per record.</param>
/// <param name="Body">The Markdown body, after the frontmatter.</param>
public sealed record ParsedTemplate(
    RenderConfig Config,
    string PassthroughFrontmatter,
    string Body);
