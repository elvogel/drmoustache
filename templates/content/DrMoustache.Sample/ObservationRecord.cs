namespace DrMoustache.Sample;

using DrMoustache.Abstractions;

/// <summary>One weather observation, projected out of SQLite.</summary>
/// <remarks>Named for the business concept, not the transport. DrMoustache
/// never learns a database was involved.</remarks>
[DrMoustacheRecord("observation")]
public sealed record ObservationRecord
{
    [Field("Station identifier — used in the output file name")]
    public required string Station { get; init; }

    [Field("Degrees Celsius")]
    public required decimal Temperature { get; init; }

    [Field] public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Absent for most rows — demonstrates a section as presence.</summary>
    [Field] public string? Note { get; init; }
}
