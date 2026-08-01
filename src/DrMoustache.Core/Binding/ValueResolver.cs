namespace DrMoustache.Core.Binding;

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

/// <summary>Resolves dotted template paths against a record.</summary>
public static class ValueResolver
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Cache = new();

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
                if (!bag.TryGetValue(segment, out current)) return false;
                continue;
            }

            var property = Cache.GetOrAdd((current.GetType(), segment), static key =>
                ResolveProperty(key.Item1, key.Item2));

            if (property is null) return false;

            current = property.GetValue(current);
        }

        value = current;
        return true;
    }

    private static PropertyInfo? ResolveProperty(Type type, string name)
    {
        // Try exact-case match first to handle types with properties differing only in case.
        // Use no IgnoreCase flag here to avoid ambiguity exceptions.
        var exactMatch = type.GetProperty(name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

        if (exactMatch is not null)
        {
            return exactMatch;
        }

        // Fall back to case-insensitive lookup, catching AmbiguousMatchException.
        try
        {
            return type.GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy);
        }
        catch (AmbiguousMatchException)
        {
            // Treat ambiguous case-insensitive paths as unresolvable (no exact match found).
            return null;
        }
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
