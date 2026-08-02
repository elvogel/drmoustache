namespace DrMoustache.Core.Delivery;

using System.Text;

/// <summary>Makes a value safe to use as a path segment.</summary>
public static class Slug
{
    public static string Make(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sb = new StringBuilder(value.Length);
        var pendingSeparator = false;

        foreach (var c in value.Trim())
        {
            if (char.IsWhiteSpace(c) || c is '/' or '\\' or '_')
            {
                pendingSeparator = sb.Length > 0;
                continue;
            }

            if (char.IsLetterOrDigit(c) || c is '-' or '.')
            {
                if (pendingSeparator)
                {
                    sb.Append('-');
                    pendingSeparator = false;
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            // Everything else — ':' '*' '?' '"' '<' '>' '|' and control
            // characters — is dropped without leaving a separator.
        }

        return sb.ToString();
    }
}
