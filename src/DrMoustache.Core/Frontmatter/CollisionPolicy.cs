namespace DrMoustache.Core.Frontmatter;

/// <summary>What to do when two records produce the same output path. There is
/// deliberately no default — choosing is the user's decision, not the tool's.</summary>
public enum CollisionPolicy
{
    /// <summary>Fail the render, naming the colliding values.</summary>
    Error,

    /// <summary>Append a short hash of the record's declared field values.
    /// Deterministic across runs regardless of arrival order.</summary>
    Suffix,

    /// <summary>Append -2, -3 by encounter order. Not stable if the provider's
    /// order changes.</summary>
    Ordinal,

    /// <summary>Last record wins; earlier ones are discarded.</summary>
    Overwrite,
}
