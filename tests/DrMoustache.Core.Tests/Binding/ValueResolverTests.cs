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

    [Fact]
    public void PrefersExactCaseMatchWhenBothCasesExist()
    {
        // A type with two properties differing only in case
        var obj = new AmbiguousCase { Total = 100, total = 200 };
        ValueResolver.TryResolve(obj, "Total", out var v).Should().BeTrue();
        v.Should().Be(100);
    }

    [Fact]
    public void ReturnsFalseWhenOnlyCaseAmbiguityExistsNoExactMatch()
    {
        // A type with two properties differing only in case, query with different case
        var obj = new AmbiguousCase { Total = 100, total = 200 };
        ValueResolver.TryResolve(obj, "TOTAL", out _).Should().BeFalse();
    }

    [Fact]
    public void DisposesEnumeratorAfterTruthiness()
    {
        var enumerable = new TrackingEnumerable();
        ValueResolver.IsTruthy(enumerable);
        enumerable.EnumeratorWasDisposed.Should().BeTrue();
    }

    // Helper type with properties differing only in case
    private sealed class AmbiguousCase
    {
        public int Total { get; set; }
        public int total { get; set; }
    }

    // Helper enumerable that tracks disposal
    private sealed class TrackingEnumerable : System.Collections.IEnumerable
    {
        public bool EnumeratorWasDisposed { get; private set; }

        public System.Collections.IEnumerator GetEnumerator() => new TrackingEnumerator(this);

        private sealed class TrackingEnumerator : System.Collections.IEnumerator, IDisposable
        {
            private readonly TrackingEnumerable _parent;

            public TrackingEnumerator(TrackingEnumerable parent) => _parent = parent;

            public object? Current => "value";

            public bool MoveNext() => false;

            public void Reset() { }

            public void Dispose() => _parent.EnumeratorWasDisposed = true;
        }
    }
}
