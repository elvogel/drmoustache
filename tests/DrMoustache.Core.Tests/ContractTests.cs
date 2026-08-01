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
            ProviderContext ctx, [EnumeratorCancellation] CancellationToken ct)
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
        using var configMgr = new Microsoft.Extensions.Configuration.ConfigurationManager();
        var ctx = new ProviderContext
        {
            Configuration = configMgr,
            Source = configMgr.GetSection("sample"),
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
        };

        var items = new List<object>();
        await foreach (var item in provider.ReadAsync(ctx, TestContext.Current.CancellationToken))
            items.Add(item);

        items.Should().ContainSingle().Which.Should().BeOfType<Sample>();
    }
}
