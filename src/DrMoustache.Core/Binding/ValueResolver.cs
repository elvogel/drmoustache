namespace DrMoustache.Core.Binding;

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

/// <summary>Resolves dotted template paths against a record.</summary>
public static class ValueResolver
{
    /// <summary>Keyed by the upper-cased segment, so '{{orderId}}' and
    /// '{{OrderId}}' share one entry instead of reflecting twice over the same
    /// type. The entry holds every candidate rather than a single resolution,
    /// which is what lets the exact-case preference below survive a
    /// case-insensitive cache key.</summary>
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo[]> Cache = new();

    public static bool TryResolve(object? root, string path, out object? value)
    {
        ArgumentNullException.ThrowIfNull(path);

        value = null;
        var current = root;

        foreach (var segment in path.Split('.'))
        {
            if (current is null) return false;

            if (current is IDictionary<string, object?> bag)
            {
                if (!TryResolveDictionary(bag, segment, out current)) return false;
                continue;
            }

            var candidates = Cache.GetOrAdd(
                (current.GetType(), segment.ToUpperInvariant()),
                static key => Candidates(key.Item1, key.Item2));

            var property = Select(candidates, segment);

            if (property is null) return false;

            current = property.GetValue(current);
        }

        value = current;
        return true;
    }

    /// <summary>Dictionary lookup matches property lookup: case-insensitively.
    /// While the exact key wins when present, '{{OrderId}}' used to return
    /// empty against a dictionary and resolve against a POCO carrying the same
    /// data — the same template behaving differently by model shape.</summary>
    private static bool TryResolveDictionary(
        IDictionary<string, object?> bag, string segment, out object? value)
    {
        if (bag.TryGetValue(segment, out value)) return true;

        foreach (var key in bag.Keys)
        {
            if (!string.Equals(key, segment, StringComparison.OrdinalIgnoreCase)) continue;

            value = bag[key];
            return true;
        }

        value = null;
        return false;
    }

    private static PropertyInfo[] Candidates(Type type, string upperName) =>
        [.. type
            .GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            .Where(p => string.Equals(p.Name, upperName, StringComparison.OrdinalIgnoreCase))];

    /// <summary>Exact case wins when a type carries properties differing only
    /// in case. Absent an exact match, a lone candidate resolves and a genuinely
    /// ambiguous one does not.</summary>
    private static PropertyInfo? Select(PropertyInfo[] candidates, string name)
    {
        foreach (var candidate in candidates)
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal)) return candidate;

        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>Section presence test. Null, false, empty string, and empty
    /// sequence are falsy; everything else is truthy.
    ///
    /// Note: This method assumes values support repeated enumeration, as callers
    /// may both check truthiness and then iterate the same value via AsSequence.
    /// </summary>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        IEnumerable e => CheckNonEmptySequence(e),
        _ => true,
    };

    private static bool CheckNonEmptySequence(IEnumerable enumerable)
    {
        var enumerator = enumerable.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    /// <summary>The value as a sequence, or null when it is not one. Strings
    /// are deliberately not sequences.
    ///
    /// Note: Callers should assume that IsTruthy may have already enumerated the
    /// value to check for emptiness. Use materialized collections (lists, arrays)
    /// to ensure repeated enumeration works correctly.
    /// </summary>
    public static IEnumerable<object?>? AsSequence(object? value) =>
        value is IEnumerable e and not string ? e.Cast<object?>() : null;
}
