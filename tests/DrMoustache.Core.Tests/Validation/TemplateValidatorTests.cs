namespace DrMoustache.Core.Tests.Validation;

using DrMoustache.Abstractions;
using DrMoustache.Core.Manifest;
using DrMoustache.Core.Template;
using DrMoustache.Core.Validation;
using FluentAssertions;
using Xunit;

public class TemplateValidatorTests
{
    // CA1812: These types are used via reflection in ManifestBuilder.FromType<T>()
#pragma warning disable CA1812
    private sealed record Inner
    {
        [Field] public required string Id { get; init; }
    }

    [DrMoustacheRecord("thing")]
    private sealed record Thing
    {
        [Field("the customer-facing order number")]
        public required string OrderId { get; init; }
        [Field] public required decimal Total { get; init; }
        [Field] public string? Note { get; init; }
        [Field] public required Inner Order { get; init; }
        [Field] public IReadOnlyList<Inner> Items { get; init; } = [];
    }
#pragma warning restore CA1812

    private static IReadOnlyList<string> Validate(string template)
        => TemplateValidator.Validate(
            TemplateLexer.Lex(template), ManifestBuilder.FromType<Thing>());

    [Fact]
    public void AcceptsAValidTemplate()
        => Validate("{{orderId}} {{order.id}} {{#items}}{{id}}{{/items}}")
            .Should().BeEmpty();

    [Fact]
    public void RejectsUnknownFieldsAndSuggestsANearMiss()
        => Validate("{{orderid}}").Should().ContainSingle()
            .Which.Should().Contain("orderid")
            .And.Contain("did you mean 'orderId'")
            .And.Contain("the customer-facing order number");

    [Fact]
    public void RejectsDirectObjectInterpolation()
        => Validate("{{order}}").Should().ContainSingle()
            .Which.Should().Contain("is an object")
            .And.Contain("{{order.id}}");

    [Fact]
    public void RejectsUnknownHelpers()
        => Validate("{{frobnicate orderId}}").Should().ContainSingle()
            .Which.Should().Contain("frobnicate");

    [Fact]
    public void RejectsDateHelperOnNonTemporalFields()
        => Validate("""{{date orderId "yyyy"}}""").Should().ContainSingle()
            .Which.Should().Contain("date").And.Contain("orderId");

    [Fact]
    public void AllowsRawTagsWithoutTypeChecking()
        => Validate("{{{order}}}").Should().BeEmpty();

    [Fact]
    public void ResolvesFieldsInsideCollectionSections()
        => Validate("{{#items}}{{id}}{{/items}}").Should().BeEmpty();

    [Fact]
    public void RejectsUnknownFieldsInsideSections()
        => Validate("{{#items}}{{nope}}{{/items}}").Should().ContainSingle();

    [Fact]
    public void AllowsDotSelfReference()
        => Validate("{{#note}}{{.}}{{/note}}").Should().BeEmpty();
}
