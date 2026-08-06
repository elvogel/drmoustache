namespace DrMoustache.Minimal;

using DrMoustache.Abstractions;

/// <summary>The shape of one row of your data.</summary>
/// <remarks>Name it for the business concept, not for where the data came
/// from. DrMoustache never needs to know about your transport.</remarks>
[DrMoustacheRecord("my-record")]
public sealed record MyRecord
{
    [Field("Used in the output file name")]
    public required string Id { get; init; }

    [Field] public required string Name { get; init; }

    [Field] public required int Value { get; init; }
}
