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
                key.Item1.GetProperty(key.Item2,
                    BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy));

            if (property is null) return false;

            current = property.GetValue(current);
        }

        value = current;
        return true;
    }

    /// <summary>Section presence test. Null, false, empty string, and empty
    /// sequence are falsy; everything else is truthy.</summary>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        IEnumerable e => e.GetEnumerator().MoveNext(),
        _ => true,
    };

    /// <summary>The value as a sequence, or null when it is not one. Strings
    /// are deliberately not sequences.</summary>
    public static IEnumerable<object?>? AsSequence(object? value) =>
        value is IEnumerable e and not string ? e.Cast<object?>() : null;
}
