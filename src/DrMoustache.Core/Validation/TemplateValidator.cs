namespace DrMoustache.Core.Validation;

using DrMoustache.Core.Frontmatter;
using DrMoustache.Core.Helpers;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Template;

/// <summary>Checks a template against a manifest without reading any data.
/// Backs the CLI's <c>check</c> verb.</summary>
public static class TemplateValidator
{
    /// <param name="nodes">The lexed template.</param>
    /// <param name="manifest">The record manifest the provider declared.</param>
    /// <param name="delivery">The template's delivery mode. It changes what the
    /// root scope is, and therefore what a bare tag means: under
    /// <see cref="DeliveryMode.Fanout"/> the root is the record itself, but
    /// under <see cref="DeliveryMode.Collate"/> the root is the planner's
    /// collate scope, which exposes only <c>count</c> and <c>records</c>.
    /// Validating a collate template against the record manifest reported
    /// 'unknown field' for both — and, because <c>{{#records}}</c> appeared to
    /// fail, never descended into the loop body, so collate templates received
    /// no field checking at all.</param>
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<TemplateNode> nodes,
        RecordManifest manifest,
        DeliveryMode delivery = DeliveryMode.Fanout)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        Walk(nodes, RootScope(manifest, delivery), prefix: null, errors);
        return errors;
    }

    /// <summary>The manifest as the template actually sees it. Mirrors
    /// <c>DeliveryPlanner</c>'s collate root scope.</summary>
    private static RecordManifest RootScope(RecordManifest manifest, DeliveryMode delivery)
        => delivery is DeliveryMode.Collate
            ? new RecordManifest(manifest.Name, [
                new ManifestField(
                    "count", FieldType.Integer, IsNullable: false,
                    "how many records the provider yielded", []),
                new ManifestField(
                    "records", FieldType.Collection, IsNullable: false,
                    $"every '{manifest.Name}' record", manifest.Fields),
            ])
            : manifest;

    private static void Walk(
        IReadOnlyList<TemplateNode> nodes, RecordManifest manifest,
        string? prefix, List<string> errors)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case InterpolationNode i:
                    CheckInterpolation(i.Path, i.Line, manifest, prefix, errors);
                    break;

                case HelperNode h:
                    CheckHelper(h, manifest, prefix, errors);
                    break;

                case SectionNode s:
                    if (Resolve(manifest, prefix, s.Path) is null)
                        errors.Add(Unknown(s.Path, s.Line, manifest, prefix));
                    else
                        Walk(s.Children, manifest, Combine(prefix, s.Path), errors);
                    break;

                // RawNode bypasses escaping and type checking by design.
                default:
                    break;
            }
        }
    }

    private static void CheckInterpolation(
        string path, int line, RecordManifest manifest,
        string? prefix, List<string> errors)
    {
        if (path == ".") return;

        var field = Resolve(manifest, prefix, path);

        if (field is null)
        {
            errors.Add(Unknown(path, line, manifest, prefix));
            return;
        }

        if (field.Type is FieldType.Object)
            errors.Add(
                $"cannot interpolate '{path}' directly (line {line})\n"
                + $"       '{path}' is an object — reference a field:  "
                + string.Join(", ", field.Children.Take(3)
                    .Select(c => $"{{{{{path}.{c.Path}}}}}")));

        if (field.Type is FieldType.Collection)
            errors.Add(
                $"cannot interpolate '{path}' directly (line {line})\n"
                + $"       '{path}' is a collection — use a section:  "
                + $"{{{{#{path}}}}}…{{{{/{path}}}}}");
    }

    private static void CheckHelper(
        HelperNode helper, RecordManifest manifest, string? prefix, List<string> errors)
    {
        if (!HelperRegistry.IsKnown(helper.Helper))
        {
            errors.Add(
                $"unknown helper '{helper.Helper}' (line {helper.Line})\n"
                + "       available: date, format, upper, lower, join, default");
            return;
        }

        var field = Resolve(manifest, prefix, helper.Path);

        if (field is null)
        {
            errors.Add(Unknown(helper.Path, helper.Line, manifest, prefix));
            return;
        }

        var mismatch = helper.Helper switch
        {
            "date" when field.Type is not FieldType.Temporal => "a temporal field",
            "format" when field.Type is not (FieldType.Integer or FieldType.Decimal)
                => "a numeric field",
            "join" when field.Type is not FieldType.Collection => "a collection",
            _ => null,
        };

        if (mismatch is not null)
            errors.Add(
                $"'{helper.Helper}' needs {mismatch}, but '{helper.Path}' is "
                + $"{field.Type} (line {helper.Line})");
    }

    /// <summary>Walks the enclosing section scopes innermost-first, exactly as
    /// <c>TemplateRenderer.Lookup</c> does at render time. Trying only the
    /// fully-combined prefix and the bare root missed every intermediate scope,
    /// so from three levels of nesting on, the validator reported 'unknown
    /// field' for tags the renderer resolves without difficulty.</summary>
    private static ManifestField? Resolve(
        RecordManifest manifest, string? prefix, string path)
    {
        var scope = prefix;

        while (true)
        {
            if (manifest.TryResolve(Combine(scope, path), out var field)) return field;
            if (scope is null) return null;

            var dot = scope.LastIndexOf('.');
            scope = dot < 0 ? null : scope[..dot];
        }
    }

    private static string Combine(string? prefix, string path) =>
        prefix is null ? path : $"{prefix}.{path}";

    private static string Unknown(
        string path, int line, RecordManifest manifest, string? prefix)
    {
        var message =
            $"unknown field '{path}' (line {line})\n"
            + $"       record '{manifest.Name}' has no such field";

        var suggestion = manifest.AllPaths()
            .Select(p => (Path: p, Distance: Distance(Leaf(p), Leaf(path))))
            .Where(x => x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .Select(x => x.Path)
            .FirstOrDefault();

        if (suggestion is null) return message;

        manifest.TryResolve(suggestion, out var field);
        var description = field?.Description is { } d ? $"  ({d})" : "";

        return $"{message}\n       did you mean '{suggestion}'?{description}";
    }

    private static string Leaf(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[(dot + 1)..];
    }

    /// <summary>Levenshtein distance, case-insensitive, for near-miss suggestions.</summary>
    private static int Distance(string a, string b)
    {
        a = a.ToUpperInvariant();
        b = b.ToUpperInvariant();

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
