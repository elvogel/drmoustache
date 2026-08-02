namespace DrMoustache.Core.Tests;

using System.Reflection;
using FluentAssertions;
using Xunit;

public class ArchitectureTests
{
    private static Assembly Core => typeof(DrMoustache.Core.Escaping.MarkdownEscaper).Assembly;

    [Theory]
    [InlineData("System.Net")]
    [InlineData("System.Net.Http")]
    [InlineData("Npgsql")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("Azure.Messaging.ServiceBus")]
    [InlineData("Azure.Core")]
    public void CoreReferencesNoIoOrTransportAssembly(string forbidden)
        => Core.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain(forbidden);

    [Theory]
    [InlineData("System.IO")]
    [InlineData("File.")]
    [InlineData("Directory.")]
    public void CoreSourcesDoNotTouchTheFilesystem(string forbidden)
    {
        var offenders = CoreSourceFiles()
            .Where(f => File.ReadAllText(f).Contains(forbidden, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty(
            $"Core performs no I/O, so '{forbidden}' must not appear in its sources");
    }

    // Transport concepts must not leak into Core's API surface.
    //
    // This inspects declared type and member NAMES via reflection rather than
    // scanning source text. A source-text scan is useless here: the standard
    // Exception(string message) constructor puts "message" in every custom
    // exception file, and Core has several. Names are also what actually
    // matters -- the constraint is about Core's public vocabulary, not about
    // whether the word appears in a comment.
    //
    // 'Row' is deliberately absent from this list. TableRow is a legitimate
    // Markdown construct, not a transport concept -- do not "fix" the omission.
    [Theory]
    [InlineData("Message")]
    [InlineData("Queue")]
    [InlineData("Endpoint")]
    public void CoreApiVocabularyHasNoTransportNouns(string noun)
    {
        var names = Core.GetTypes()
            .SelectMany(t => new[] { t.Name }.Concat(
                t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static
                        | BindingFlags.DeclaredOnly)
                    .Select(m => m.Name)))
            .ToList();

        names.Should().NotBeEmpty("the scan must actually find Core's API surface");

        names.Where(n => n.Contains(noun, StringComparison.Ordinal))
            .Should().BeEmpty(
                $"'{noun}' is a transport concept and must not leak into Core");
    }

    private static List<string> CoreSourceFiles()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(FindSourceRoot(), "src", "DrMoustache.Core"),
                "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToList();

        files.Should().NotBeEmpty("the scan must actually find Core's sources");
        return files;
    }

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DrMoustache.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }
}
