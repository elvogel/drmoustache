namespace DrMoustache.Core.Frontmatter;

public enum DeliveryMode
{
    /// <summary>One document per record.</summary>
    Fanout,

    /// <summary>All records into one document.</summary>
    Collate,
}
