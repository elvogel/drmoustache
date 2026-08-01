namespace DrMoustache.Core.Tests.Helpers;

using System.Globalization;
using DrMoustache.Core.Helpers;
using DrMoustache.Core.Template;
using FluentAssertions;
using Xunit;

public class HelperRegistryTests
{
    private static readonly string[] ThreeStringArray = ["a", "b", "c"];
    private static readonly string[] TwoStringArray = ["a", "b"];

    [Theory]
    [InlineData("date")]
    [InlineData("format")]
    [InlineData("upper")]
    [InlineData("lower")]
    [InlineData("join")]
    [InlineData("default")]
    public void KnownHelpersAreRecognised(string name)
        => HelperRegistry.IsKnown(name).Should().BeTrue();

    [Fact]
    public void UnknownHelperIsNotRecognised()
        => HelperRegistry.IsKnown("frobnicate").Should().BeFalse();

    [Fact]
    public void ApplyingAnUnknownHelperThrows()
    {
        var act = () => HelperRegistry.Apply("frobnicate", "x", null, 3);
        act.Should().Throw<TemplateException>()
            .WithMessage("*frobnicate*")
            .And.Message.Should().Contain("date").And.Contain("default");
    }

    [Fact]
    public void DateFormatsTemporalValues()
        => HelperRegistry.Apply(
                "date", new DateTimeOffset(2026, 8, 1, 14, 30, 0, TimeSpan.Zero),
                "yyyy-MM-dd HH:mm", 1)
            .Should().Be("2026-08-01 14:30");

    [Fact]
    public void DateWithoutAnArgumentUsesRoundTripFormat()
        => HelperRegistry.Apply("date", new DateOnly(2026, 8, 1), null, 1)
            .Should().Be("2026-08-01");

    [Fact]
    public void DateRejectsNonTemporalValues()
    {
        var act = () => HelperRegistry.Apply("date", "not a date", "yyyy", 7);
        act.Should().Throw<TemplateException>().WithMessage("*date*line 7*");
    }

    [Fact]
    public void FormatFormatsNumbers()
        => HelperRegistry.Apply("format", 1234.5m, "N2", 1)
            .Should().Be(1234.5m.ToString("N2", CultureInfo.CurrentCulture));

    [Fact]
    public void FormatRejectsNonNumericValues()
    {
        var act = () => HelperRegistry.Apply("format", "abc", "N2", 4);
        act.Should().Throw<TemplateException>().WithMessage("*format*line 4*");
    }

    [Fact]
    public void UpperAndLowerUseInvariantCasing()
    {
        HelperRegistry.Apply("upper", "abc", null, 1).Should().Be("ABC");
        HelperRegistry.Apply("lower", "ABC", null, 1).Should().Be("abc");
    }

    [Fact]
    public void JoinUsesTheSuppliedSeparator()
        => HelperRegistry.Apply("join", ThreeStringArray, ", ", 1)
            .Should().Be("a, b, c");

    [Fact]
    public void JoinDefaultsToCommaSpace()
        => HelperRegistry.Apply("join", TwoStringArray, null, 1).Should().Be("a, b");

    [Fact]
    public void JoinRejectsNonSequences()
    {
        var act = () => HelperRegistry.Apply("join", 42, ", ", 9);
        act.Should().Throw<TemplateException>().WithMessage("*join*line 9*");
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData("x", "x")]
    public void DefaultSubstitutesForNullAndEmpty(object? value, string expected)
        => HelperRegistry.Apply("default", value, "none", 1).Should().Be(expected);

    [Fact]
    public void NullValuesRenderEmptyForNonDefaultHelpers()
        => HelperRegistry.Apply("upper", null, null, 1).Should().BeEmpty();
}
