namespace DrMoustache.Rendering;

using System.Globalization;
using System.Text;

/// <param name="Source">Which source the record came from. One project can
/// render several sources in one pass.</param>
/// <param name="Ordinal">Zero-based position in the provider's sequence — the
/// only stable identifier available before a record is bound.</param>
/// <param name="Reason">Why the record was skipped.</param>
public sealed record RecordSkip(string Source, int Ordinal, string Reason);

/// <param name="DocumentsWritten">The number of documents that were successfully rendered.</param>
/// <param name="Warnings">A list of warnings generated during rendering.</param>
/// <param name="Skips">Records that were individually unusable. The read
/// continued, so what was lost is known exactly.</param>
/// <param name="Faults">Reads that died partway — a database that went away,
/// credentials that expired. Unlike a skip, a fault leaves it unknown how many
/// records were never seen, so it fails the run whatever
/// <see cref="DrMoustacheOptions.Strict"/> says.</param>
/// <param name="Success">Accounts for <see cref="DrMoustacheOptions.Strict"/>,
/// so a caller does not have to remember to compare skips against an option
/// set somewhere else. False whenever <paramref name="Faults"/> is non-empty.</param>
public sealed record RenderResult(
    int DocumentsWritten,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RecordSkip> Skips,
    IReadOnlyList<string> Faults,
    bool Success)
{
    /// <summary>A human-readable report of what happened.</summary>
    public string Summary
    {
        get
        {
            var sb = new StringBuilder();

            sb.Append(CultureInfo.InvariantCulture,
                $"rendered {DocumentsWritten} document{(DocumentsWritten == 1 ? "" : "s")}");

            if (Skips.Count > 0)
                sb.Append(CultureInfo.InvariantCulture, $"  ({Skips.Count} skipped)");

            if (Faults.Count > 0)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"  ({Faults.Count} fault{(Faults.Count == 1 ? "" : "s")})");
            }

            sb.AppendLine();

            // Faults first: a dead read explains an implausibly small document
            // count, and reading that before the skip list saves a false trail.
            foreach (var fault in Faults)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  FAULT    {fault}");

            foreach (var skip in Skips)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"  skipped  {skip.Source} record {skip.Ordinal}  {skip.Reason}");
            }

            foreach (var warning in Warnings)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  warning  {warning}");

            return sb.ToString();
        }
    }
}

/// <param name="TemplatesChecked">The number of templates that were checked.</param>
/// <param name="Problems">A list of problems found during template checking.</param>
public sealed record CheckResult(int TemplatesChecked, IReadOnlyList<string> Problems)
{
    public bool Success => Problems.Count == 0;

    public string Summary
    {
        get
        {
            if (Success)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"checked {TemplatesChecked} template"
                    + $"{(TemplatesChecked == 1 ? "" : "s")} — no problems found")
                    + Environment.NewLine;
            }

            var sb = new StringBuilder();

            sb.AppendLine(CultureInfo.InvariantCulture,
                $"checked {TemplatesChecked} template"
                + $"{(TemplatesChecked == 1 ? "" : "s")} — {Problems.Count} problem"
                + $"{(Problems.Count == 1 ? "" : "s")}");

            foreach (var problem in Problems)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {problem}");

            return sb.ToString();
        }
    }
}
