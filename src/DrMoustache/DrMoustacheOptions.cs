namespace DrMoustache;

using System.Globalization;

/// <summary>Configures a render. Binds from a <c>DrMoustache</c>
/// configuration section with no extra work.</summary>
public sealed class DrMoustacheOptions
{
    /// <summary>Directory searched recursively for templates.</summary>
    public string TemplateDirectory { get; set; } = "templates";

    /// <summary>Directory rendered documents are written beneath.</summary>
    public string OutputDirectory { get; set; } = "dist";

    /// <summary>Treat any skipped record as a failure. The CI posture.</summary>
    /// <remarks>A skip means a record never reached the output. A warning means
    /// something was resolved without losing data, so warnings never fail.</remarks>
    public bool Strict { get; set; }

    /// <summary>Formatting culture for numbers and dates.</summary>
    /// <remarks>Null means <see cref="CultureInfo.InvariantCulture"/>, so the same
    /// templates and data produce byte-identical output on every machine. Set this
    /// only to localise deliberately.</remarks>
    public CultureInfo? Culture { get; set; }
}
