namespace DrMoustache.Discovery;

using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Template;

/// <param name="Path">Absolute path, used in error messages.</param>
/// <param name="Parsed">The parsed template configuration and content.</param>
internal sealed record TemplateFile(string Path, ParsedTemplate Parsed)
{
    private const string ReservedKey = "drmoustache:";

    /// <summary>Finds every renderable template beneath a directory.</summary>
    /// <remarks>Markdown files without a <c>drmoustache:</c> block are skipped
    /// rather than rejected: a template directory legitimately holds
    /// hand-written pages too. Directories beginning with an underscore are
    /// skipped as partials.</remarks>
    internal static IReadOnlyList<TemplateFile> Discover(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
            throw new RenderException($"template directory not found: {directory}");

        var templates = new List<TemplateFile>();

        foreach (var path in Directory
            .EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
            .Where(p => !IsUnderPartials(directory, p))
            .Order(StringComparer.Ordinal))
        {
            var content = File.ReadAllText(path);

            if (!DeclaresTemplate(content))
                continue;

            try
            {
                templates.Add(new TemplateFile(path, FrontmatterParser.Parse(content)));
            }
            catch (TemplateException ex)
            {
                throw new RenderException($"{path}: {ex.Message}", ex);
            }
        }

        return templates;
    }

    /// <summary>True when the file declares a reserved block in its frontmatter.</summary>
    /// <remarks>Only the frontmatter region is searched. Scanning the whole file
    /// would match a page that merely documents the format in a fenced code
    /// block — and because such a page has no reserved block, parsing it would
    /// throw and abort discovery for every template beside it.</remarks>
    private static bool DeclaresTemplate(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal)) return false;

        var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return false;

        return content.AsSpan(0, end).Contains(ReservedKey, StringComparison.Ordinal);
    }

    private static bool IsUnderPartials(string root, string path)
        => System.IO.Path.GetRelativePath(root, path)
            .Split(System.IO.Path.DirectorySeparatorChar,
                   System.IO.Path.AltDirectorySeparatorChar)
            .SkipLast(1)
            .Any(segment => segment.StartsWith('_'));
}
