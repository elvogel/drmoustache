namespace DrMoustache.Tests.Output;

using DrMoustache.Core.Rendering;
using DrMoustache.Output;
using FluentAssertions;
using Xunit;

public sealed class DocumentWriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("drm-out-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private DocumentWriter Writer() => new(_root);

    [Fact]
    public async Task WritesADocument()
    {
        var written = await Writer().WriteAllAsync(
            [new RenderedDocument("a.md", "# Hello")],
            TestContext.Current.CancellationToken);

        written.Should().Be(1);
        (await File.ReadAllTextAsync(
            Path.Combine(_root, "a.md"), TestContext.Current.CancellationToken))
            .Should().Be("# Hello");
    }

    [Fact]
    public async Task CreatesNestedDirectories()
    {
        await Writer().WriteAllAsync(
            [new RenderedDocument("dlq/expired/a.md", "x")],
            TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(_root, "dlq", "expired", "a.md")).Should().BeTrue();
    }

    [Fact]
    public async Task OverwritesAnExistingDocument()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_root, "a.md"), "old", TestContext.Current.CancellationToken);

        await Writer().WriteAllAsync(
            [new RenderedDocument("a.md", "new")], TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(
            Path.Combine(_root, "a.md"), TestContext.Current.CancellationToken))
            .Should().Be("new");
    }

    [Fact]
    public async Task LeavesNoTemporaryFilesBehind()
    {
        await Writer().WriteAllAsync(
            [new RenderedDocument("a.md", "x"), new RenderedDocument("b/c.md", "y")],
            TestContext.Current.CancellationToken);

        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo(["a.md", "c.md"]);
    }

    [Fact]
    public async Task WritesUtf8WithoutAByteOrderMark()
    {
        await Writer().WriteAllAsync(
            [new RenderedDocument("a.md", "café")], TestContext.Current.CancellationToken);

        var bytes = await File.ReadAllBytesAsync(
            Path.Combine(_root, "a.md"), TestContext.Current.CancellationToken);

        // Verify the file doesn't start with UTF-8 BOM
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        bytes.Take(Math.Min(3, bytes.Length)).SequenceEqual(bom).Should().BeFalse();
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("a/../../escape.md")]
    [InlineData("/absolute.md")]
    public async Task RefusesToWriteOutsideTheOutputRoot(string path)
    {
#pragma warning disable CA2007 // Lambda in test context; xUnit1030 forbids ConfigureAwait(false) in test methods
        var act = async () => await Writer().WriteAllAsync(
            [new RenderedDocument(path, "x")], TestContext.Current.CancellationToken);
#pragma warning restore CA2007

        (await act.Should().ThrowAsync<OutputException>()).WithMessage("*outside*");
    }

    [Fact]
    public async Task AnEmptyListWritesNothingAndCreatesNoRoot()
    {
        var missing = Path.Combine(_root, "nested-root");

        var written = await new DocumentWriter(missing).WriteAllAsync(
            [], TestContext.Current.CancellationToken);

        written.Should().Be(0);
        Directory.Exists(missing).Should().BeFalse();
    }
}
