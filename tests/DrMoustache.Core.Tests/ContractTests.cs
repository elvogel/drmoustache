namespace DrMoustache.Core.Tests;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using FluentAssertions;
using Xunit;

public class ContractTests
{
    [DrMoustacheRecord("sample")]
    private sealed record Sample
    {
        [Field] public required string Id { get; init; }
        [Field("a human note")] public string? Note { get; init; }
    }

    private sealed class SampleProvider : DataProvider<Sample>
    {
        public override string Name => "sample-provider";

        public override async IAsyncEnumerable<Sample> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new Sample { Id = "a" };
        }
    }

    [Fact]
    public void RecordAttributeCarriesName()
    {
        var attr = typeof(Sample).GetCustomAttributes(typeof(DrMoustacheRecordAttribute), false)
            .Cast<DrMoustacheRecordAttribute>().Single();
        attr.Name.Should().Be("sample");
    }

    [Fact]
    public void FieldAttributeCarriesOptionalDescription()
    {
        var note = typeof(Sample).GetProperty(nameof(Sample.Note))!;
        var attr = note.GetCustomAttributes(typeof(FieldAttribute), false)
            .Cast<FieldAttribute>().Single();
        attr.Description.Should().Be("a human note");

        var id = typeof(Sample).GetProperty(nameof(Sample.Id))!;
        var idAttr = id.GetCustomAttributes(typeof(FieldAttribute), false)
            .Cast<FieldAttribute>().Single();
        idAttr.Description.Should().BeNull();
    }

    [Fact]
    public void TypedBaseExposesRecordTypeThroughInterface()
    {
        IDataProvider provider = new SampleProvider();
        provider.Name.Should().Be("sample-provider");
        provider.RecordType.Should().Be<Sample>();
    }

    [Fact]
    public async Task TypedBaseBridgesToObjectSequence()
    {
        IDataProvider provider = new SampleProvider();

        var items = new List<object>();
        await foreach (var item in provider.ReadAsync(TestContext.Current.CancellationToken))
            items.Add(item);

        items.Should().ContainSingle().Which.Should().BeOfType<Sample>();
    }

    [Fact]
    public void AbstractionsHasNoPackageDependencies()
    {
        var referenced = typeof(IDataProvider).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null
                && !n.StartsWith("System.", StringComparison.Ordinal)
                && !string.Equals(n, "netstandard", StringComparison.Ordinal))
            .ToList();

        referenced.Should().BeEmpty(
            "the contract package must be referenceable without dragging anything in");
    }
}
