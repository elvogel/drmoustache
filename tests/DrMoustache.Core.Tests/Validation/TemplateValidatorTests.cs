namespace DrMoustache.Core.Tests.Validation;

using DrMoustache.Abstractions;
using DrMoustache.Core.Frontmatter;
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

    private static IReadOnlyList<string> Validate(
        string template, DeliveryMode delivery = DeliveryMode.Fanout)
        => TemplateValidator.Validate(
            TemplateLexer.Lex(template), ManifestBuilder.FromType<Thing>(), delivery);

    [Fact]
    public void AcceptsAValidTemplate()
        => Validate("{{orderId}} {{order.id}} {{#items}}{{id}}{{/items}}")
            .Should().BeEmpty();

    [Fact]
    public void RejectsUnknownFieldsAndSuggestsANearMiss()
        => Validate("{{ordreId}}").Should().ContainSingle()
            .Which.Should().Contain("ordreId")
            .And.Contain("did you mean 'orderId'")
            .And.Contain("the customer-facing order number");

    /// <summary>The renderer resolves paths case-insensitively, so 'check'
    /// must too: '{{OrderId}}' used to fail the CI gate and then render
    /// perfectly.</summary>
    [Fact]
    public void AcceptsFieldsReferencedWithDifferentCasing()
        => Validate("{{OrderId}} {{Order.Id}}").Should().BeEmpty();

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

    /// <summary>The renderer walks every enclosing scope innermost-first, so a
    /// tag on the record's root resolves from inside two nested sections. The
    /// validator tried only the fully-combined prefix and the bare root, so
    /// from three levels of nesting on it invented errors the renderer
    /// disagreed with.</summary>
    [Fact]
    public void ResolvesThroughIntermediateScopesAtThreeLevelsOfNesting()
        => Validate("{{#items}}{{#order}}{{id}}{{/order}}{{/items}}").Should().BeEmpty();

    [Fact]
    public void ResolvesAnOuterFieldFromInsideANestedSection()
        => Validate("{{#items}}{{#order}}{{orderId}}{{/order}}{{/items}}").Should().BeEmpty();

    // --- collate delivery -------------------------------------------------
    //
    // Under collate the root scope is the planner's collate scope, not the
    // record. Validating a collate template against the record manifest
    // reported 'unknown field' for 'count' and 'records' -- and, because
    // '{{#records}}' appeared to fail, never descended into the loop body, so
    // collate templates got no field checking whatsoever.

    private static IReadOnlyList<string> ValidateCollate(string template)
        => TemplateValidator.Validate(
            TemplateLexer.Lex(template), ManifestBuilder.FromType<Thing>(),
            DeliveryMode.Collate);

    [Fact]
    public void AcceptsTheDesignsOwnCollateExample()
        => ValidateCollate(
                "# Dead-letter queue — {{count}} messages\n\n"
                + "| Order | Total |\n|-------|-------|\n"
                + "{{#records}}| {{orderId}} | {{total}} |\n{{/records}}")
            .Should().BeEmpty();

    [Fact]
    public void StillChecksFieldsInsideACollateRecordsSection()
        => ValidateCollate("{{#records}}{{nope}}{{/records}}")
            .Should().ContainSingle().Which.Should().Contain("nope");

    [Fact]
    public void RejectsARecordFieldAtACollateTemplatesRoot()
        => ValidateCollate("{{orderId}}").Should().ContainSingle()
            .Which.Should().Contain("orderId");

    [Fact]
    public void DoesNotOfferCountOrRecordsToAFanoutTemplate()
        => Validate("{{count}}").Should().ContainSingle()
            .Which.Should().Contain("count");
}
