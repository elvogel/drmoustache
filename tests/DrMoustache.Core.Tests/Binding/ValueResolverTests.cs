namespace DrMoustache.Core.Tests.Binding;

using DrMoustache.Core.Binding;
using FluentAssertions;
using Xunit;

public class ValueResolverTests
{
    private static readonly string[] ExpectedTags = ["a", "b"];
    private static readonly string[] SingleTag = ["a"];

    private sealed record Inner(string Id, decimal Total);

    private sealed record Outer(
        string OrderId, string? PromoCode, Inner Order, IReadOnlyList<string> Tags);

    private static Outer Sample() =>
        new("ORD-1", null, new Inner("i-1", 12.5m), ExpectedTags);

    [Fact]
    public void ResolvesTopLevelProperty()
    {
        ValueResolver.TryResolve(Sample(), "orderId", out var v).Should().BeTrue();
        v.Should().Be("ORD-1");
    }

    [Fact]
    public void MatchesCaseInsensitively()
        => ValueResolver.TryResolve(Sample(), "OrderId", out _).Should().BeTrue();

    [Fact]
    public void ResolvesNestedPath()
    {
        ValueResolver.TryResolve(Sample(), "order.total", out var v).Should().BeTrue();
        v.Should().Be(12.5m);
    }

    [Fact]
    public void ResolvesNullPropertyAsPresentButNull()
    {
        ValueResolver.TryResolve(Sample(), "promoCode", out var v).Should().BeTrue();
        v.Should().BeNull();
    }

    [Fact]
    public void FailsOnUnknownPath()
        => ValueResolver.TryResolve(Sample(), "nope", out _).Should().BeFalse();

    [Fact]
    public void FailsWhenDescendingThroughNull()
        => ValueResolver.TryResolve(Sample(), "promoCode.length", out _).Should().BeFalse();

    [Fact]
    public void ResolvesDictionaryEntries()
    {
        var bag = new Dictionary<string, object?> { ["name"] = "x" };
        ValueResolver.TryResolve(bag, "name", out var v).Should().BeTrue();
        v.Should().Be("x");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData("", false)]
    [InlineData("x", true)]
    [InlineData(0, true)]
    public void TruthinessFollowsMustacheRules(object? value, bool expected)
        => ValueResolver.IsTruthy(value).Should().Be(expected);

    [Fact]
    public void EmptySequenceIsFalsy()
        => ValueResolver.IsTruthy(Array.Empty<string>()).Should().BeFalse();

    [Fact]
    public void NonEmptySequenceIsTruthy()
        => ValueResolver.IsTruthy(SingleTag).Should().BeTrue();

    [Fact]
    public void SequencesAreEnumerable()
    {
        ValueResolver.TryResolve(Sample(), "tags", out var v).Should().BeTrue();
        ValueResolver.AsSequence(v).Should().BeEquivalentTo(ExpectedTags);
    }

    [Fact]
    public void StringsAreNotSequences()
        => ValueResolver.AsSequence("abc").Should().BeNull();
}
