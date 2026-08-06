namespace DrMoustache.Tests.Validation;

using DrMoustache.Abstractions;
using DrMoustache.Core.Manifest;
using DrMoustache.Validation;
using FluentAssertions;
using Xunit;

public class RecordValidatorTests
{
    private sealed record Inner
    {
        [Field] public required string Id { get; init; }
    }

    [DrMoustacheRecord("thing")]
    private sealed record Thing
    {
        [Field("the order number")] public required string OrderId { get; init; }
        [Field] public required decimal Total { get; init; }
        [Field] public string? Note { get; init; }
        [Field] public Inner? Nested { get; init; }
    }

    private static RecordManifest Manifest => ManifestBuilder.FromType<Thing>();

    [Fact]
    public void AcceptsACompleteRecord()
        => RecordValidator.Validate(new Thing { OrderId = "a", Total = 1m }, Manifest)
            .Should().BeNull();

    [Fact]
    public void AcceptsANullNullableField()
        => RecordValidator.Validate(
                new Thing { OrderId = "a", Total = 1m, Note = null }, Manifest)
            .Should().BeNull();

    [Fact]
    public void RejectsANullRequiredField()
        => RecordValidator.Validate(new Thing { OrderId = null!, Total = 1m }, Manifest)
            .Should().Contain("orderId").And.Contain("required");

    [Fact]
    public void NamesTheFieldDescriptionWhenOneExists()
        => RecordValidator.Validate(new Thing { OrderId = null!, Total = 1m }, Manifest)
            .Should().Contain("the order number");

    [Fact]
    public void DoesNotDescendIntoANullNullableObject()
        => RecordValidator.Validate(
                new Thing { OrderId = "a", Total = 1m, Nested = null }, Manifest)
            .Should().BeNull();

    [Fact]
    public void RejectsANullRequiredFieldInsideANestedObject()
        => RecordValidator.Validate(
                new Thing { OrderId = "a", Total = 1m, Nested = new Inner { Id = null! } },
                Manifest)
            .Should().Contain("nested.id");
}
