namespace DrMoustache.Core.Manifest;

using System.Diagnostics.CodeAnalysis;

/// <summary>The declared shape of a provider's records. Plain data — holds no
/// reflection handles, so a source generator can emit it later.</summary>
public sealed class RecordManifest(string name, IReadOnlyList<ManifestField> fields)
{
    public string Name { get; } = name;

    public IReadOnlyList<ManifestField> Fields { get; } = fields;

    public bool TryResolve(string path, [NotNullWhen(true)] out ManifestField? field)
    {
        ArgumentNullException.ThrowIfNull(path);

        var current = Fields;
        field = null;

        foreach (var segment in path.Split('.'))
        {
            var match = current.FirstOrDefault(f =>
                string.Equals(f.Path, segment, StringComparison.Ordinal));

            if (match is null)
            {
                field = null;
                return false;
            }

            field = match;
            current = match.Children;
        }

        return field is not null;
    }

    public IEnumerable<string> AllPaths() => Walk(Fields, prefix: null);

    private static IEnumerable<string> Walk(
        IReadOnlyList<ManifestField> fields, string? prefix)
    {
        foreach (var f in fields)
        {
            var path = prefix is null ? f.Path : $"{prefix}.{f.Path}";
            yield return path;

            foreach (var child in Walk(f.Children, path))
                yield return child;
        }
    }
}
