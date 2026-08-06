namespace DrMoustache.Output;

using System.Text;
using DrMoustache.Core.Rendering;

/// <summary>Writes rendered documents beneath a root directory.</summary>
/// <remarks>Each document is written to a temporary file in its destination
/// directory and then moved into place, so a failure partway through never
/// leaves a truncated file that looks valid.</remarks>
internal sealed class DocumentWriter(string outputRoot)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _outputRoot = outputRoot
        ?? throw new ArgumentNullException(nameof(outputRoot));

    internal async Task<int> WriteAllAsync(
        IReadOnlyList<RenderedDocument> documents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0) return 0;

        var root = Path.GetFullPath(_outputRoot);
        Directory.CreateDirectory(root);

        foreach (var document in documents)
            await WriteAsync(root, document, ct).ConfigureAwait(false);

        return documents.Count;
    }

    private static async Task WriteAsync(
        string root, RenderedDocument document, CancellationToken ct)
    {
        var destination = Resolve(root, document.Path);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // Same directory as the destination, so the move is on one volume
        // and therefore atomic.
        var temporary = destination + ".drm-tmp";

        try
        {
            await File.WriteAllTextAsync(temporary, document.Content, Utf8NoBom, ct)
                .ConfigureAwait(false);

            File.Move(temporary, destination, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new OutputException($"could not write '{destination}': {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException) { /* best effort */ }
            }
        }
    }

    /// <summary>Resolves a document path against the root and refuses anything
    /// that escapes it.</summary>
    /// <remarks>DeliveryPlanner already slugifies each interpolated value, so
    /// this should be unreachable. It is the last place a bug anywhere upstream
    /// could write outside the output directory, which makes a second check
    /// worth its cost.</remarks>
    private static string Resolve(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new OutputException(
                $"refusing to write '{relativePath}': absolute paths fall outside "
                + $"the output directory '{root}'");
        }

        var combined = Path.GetFullPath(Path.Combine(root, relativePath));

        var rooted = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rooted, StringComparison.Ordinal))
        {
            throw new OutputException(
                $"refusing to write '{relativePath}': it resolves outside "
                + $"the output directory '{root}'");
        }

        return combined;
    }
}
