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

        // A value of "." or ".." (or any run of leading dots) must not
        // survive as a path segment: those are directory-traversal tokens
        // to a filesystem, and a lone leading dot silently creates a
        // hidden file. Strip leading dots; if nothing else is left, the
        // slug is empty.
        var slug = sb.ToString().TrimStart('.');
        return slug.Length == 0 || slug.All(c => c == '.') ? "" : slug;
    }
}
