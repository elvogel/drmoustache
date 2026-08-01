#pragma warning disable CS8602 // Dereference of a possibly null reference; FluentAssertions assertions guarantee non-null on test pass

namespace DrMoustache.Core.Tests.Manifest;

using DrMoustache.Core.Manifest;
using FluentAssertions;
using Xunit;

public class RecordManifestTests
{
    private static RecordManifest Build() => new("failed-order",
    [
        new ManifestField("orderId", FieldType.String, false, "the order number", []),
        new ManifestField("total", FieldType.Decimal, false, null, []),
        new ManifestField("promoCode", FieldType.String, true, null, []),
        new ManifestField("order", FieldType.Object, false, null,
        [
            new ManifestField("id", FieldType.String, false, null, []),
            new ManifestField("total", FieldType.Decimal, false, null, []),
        ]),
        new ManifestField("items", FieldType.Collection, false, null,
        [
            new ManifestField("name", FieldType.String, false, null, []),
        ]),
    ]);

    [Fact]
    public void ResolvesTopLevelField()
    {
        Build().TryResolve("orderId", out var f).Should().BeTrue();
        f.Type.Should().Be(FieldType.String);
        f.Description.Should().Be("the order number");
    }

    [Fact]
    public void ResolvesNestedFieldByDottedPath()
    {
        Build().TryResolve("order.total", out var f).Should().BeTrue();
        f.Type.Should().Be(FieldType.Decimal);
    }

    [Fact]
    public void FailsOnUnknownPath()
    {
        Build().TryResolve("nope", out _).Should().BeFalse();
        Build().TryResolve("order.nope", out _).Should().BeFalse();
    }

    [Fact]
    public void AllPathsIncludesNestedAndCollectionMembers()
    {
        Build().AllPaths().Should().BeEquivalentTo(
        [
            "orderId", "total", "promoCode",
            "order", "order.id", "order.total",
            "items", "items.name",
        ]);
    }

    [Fact]
    public void NullabilityIsRecorded()
    {
        Build().TryResolve("promoCode", out var f).Should().BeTrue();
        f.IsNullable.Should().BeTrue();
        Build().TryResolve("orderId", out var g).Should().BeTrue();
        g.IsNullable.Should().BeFalse();
    }
}
