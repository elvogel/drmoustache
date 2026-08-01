namespace DrMoustache.Core.Tests.Manifest;

using DrMoustache.Abstractions;
using DrMoustache.Core.Manifest;
using FluentAssertions;
using Xunit;

public class ManifestBuilderTests
{
    // CA1812: These types are used via reflection in ManifestBuilder.FromType<T>()
#pragma warning disable CA1812
    private sealed record LineItem
    {
        [Field] public required string Name { get; init; }
        [Field] public int Qty { get; init; }
    }

    private sealed record OrderInfo
    {
        [Field] public required string Id { get; init; }
        [Field] public decimal Total { get; init; }
    }

    [DrMoustacheRecord("failed-order")]
    private sealed record FailedOrder
    {
        [Field] public required long SequenceNumber { get; init; }
        [Field("Why it dead-lettered")] public required string Reason { get; init; }
        [Field] public required decimal Total { get; init; }
        [Field] public DateTimeOffset PlacedAt { get; init; }
        [Field] public bool IsRetryable { get; init; }
        [Field] public Guid TraceId { get; init; }
        [Field] public string? PromoCode { get; init; }
        [Field] public required OrderInfo Order { get; init; }
        [Field] public IReadOnlyList<LineItem> Items { get; init; } = [];
        public string NotExposed { get; init; } = "";
    }

    private sealed record Unannotated;
#pragma warning restore CA1812

    [Fact]
    public void UsesTheDeclaredRecordName()
        => ManifestBuilder.FromType<FailedOrder>().Name.Should().Be("failed-order");

    [Theory]
    [InlineData("sequenceNumber", FieldType.Integer)]
    [InlineData("reason", FieldType.String)]
    [InlineData("total", FieldType.Decimal)]
    [InlineData("placedAt", FieldType.Temporal)]
    [InlineData("isRetryable", FieldType.Boolean)]
    [InlineData("traceId", FieldType.Guid)]
    [InlineData("order", FieldType.Object)]
    [InlineData("items", FieldType.Collection)]
    public void MapsClrTypesToFieldTypes(string path, FieldType expected)
    {
        ManifestBuilder.FromType<FailedOrder>().TryResolve(path, out var f).Should().BeTrue();
        f!.Type.Should().Be(expected);
    }

    [Fact]
    public void CamelCasesPropertyNames()
        => ManifestBuilder.FromType<FailedOrder>().AllPaths()
            .Should().Contain("sequenceNumber").And.NotContain("SequenceNumber");

    [Fact]
    public void ExposesOnlyAnnotatedProperties()
        => ManifestBuilder.FromType<FailedOrder>().AllPaths()
            .Should().NotContain("notExposed");

    [Fact]
    public void RecordsNullability()
    {
        var m = ManifestBuilder.FromType<FailedOrder>();
        m.TryResolve("promoCode", out var nullable).Should().BeTrue();
        nullable!.IsNullable.Should().BeTrue();
        m.TryResolve("reason", out var required).Should().BeTrue();
        required!.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void CarriesDescriptions()
    {
        ManifestBuilder.FromType<FailedOrder>().TryResolve("reason", out var f).Should().BeTrue();
        f!.Description.Should().Be("Why it dead-lettered");
    }

    [Fact]
    public void DescendsIntoNestedObjectsAndCollections()
        => ManifestBuilder.FromType<FailedOrder>().AllPaths()
            .Should().Contain(["order.id", "order.total", "items.name", "items.qty"]);

    [Fact]
    public void RejectsTypesWithoutTheRecordAttribute()
    {
        var act = () => ManifestBuilder.FromType<Unannotated>();
        act.Should().Throw<ManifestException>()
            .WithMessage("*[DrMoustacheRecord]*");
    }
}
