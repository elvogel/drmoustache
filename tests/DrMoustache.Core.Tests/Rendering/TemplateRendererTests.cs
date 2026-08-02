namespace DrMoustache.Core.Tests.Rendering;

using System.Globalization;
using DrMoustache.Core.Rendering;
using DrMoustache.Core.Template;
using FluentAssertions;
using Xunit;

public class TemplateRendererTests
{
    private sealed record Item(string Name, int Qty);

    private sealed record Model(
        string Title, string? Note, decimal Total, IReadOnlyList<Item> Items);

    private sealed record FlagModel(bool Flag);

    private static string Render(string template, object? model)
        => TemplateRenderer.Render(TemplateLexer.Lex(template), model);

    [Fact]
    public void InterpolatesAValue()
        => Render("Hello {{title}}", new Model("world", null, 0, []))
            .Should().Be("Hello world");

    [Fact]
    public void EscapesAccordingToContext()
        => Render("| {{title}} |", new Model("a | b", null, 0, []))
            .Should().Be(@"| a \| b |");

    [Fact]
    public void DoesNotEscapeInsideFencedCode()
        => Render("```\n{{title}}\n```", new Model("a | b", null, 0, []))
            .Should().Contain("a | b");

    [Fact]
    public void TripleBracesBypassEscaping()
        => Render("| {{{title}}} |", new Model("a | b", null, 0, []))
            .Should().Be("| a | b |");

    [Fact]
    public void EscapesLeadingBlockMarkersAtLineStart()
        => Render("{{title}}", new Model("#1 pick", null, 0, []))
            .Should().Be(@"\#1 pick");

    [Fact]
    public void NullValuesRenderEmpty()
        => Render("[{{note}}]", new Model("x", null, 0, [])).Should().Be("[]");

    [Fact]
    public void UnknownPathsRenderEmpty()
        => Render("[{{nope}}]", new Model("x", null, 0, [])).Should().Be("[]");

    [Fact]
    public void SectionsIterateCollections()
        => Render("{{#items}}{{name}};{{/items}}",
                new Model("x", null, 0, [new("a", 1), new("b", 2)]))
            .Should().Be("a;b;");

    [Fact]
    public void SectionsProduceOneTableRowPerItem()
    {
        var output = Render(
            "| Item | Qty |\n|---|---|\n{{#items}}| {{name}} | {{qty}} |\n{{/items}}",
            new Model("x", null, 0, [new("a", 1), new("b", 2)]));

        output.Should().Be("| Item | Qty |\n|---|---|\n| a | 1 |\n| b | 2 |\n");
    }

    [Fact]
    public void EmptySectionsRenderNothing()
        => Render("[{{#items}}x{{/items}}]", new Model("x", null, 0, []))
            .Should().Be("[]");

    [Fact]
    public void InvertedSectionsRenderWhenFalsy()
        => Render("{{^items}}none{{/items}}", new Model("x", null, 0, []))
            .Should().Be("none");

    [Fact]
    public void InvertedSectionsAreSkippedWhenTruthy()
        => Render("{{^items}}none{{/items}}", new Model("x", null, 0, [new("a", 1)]))
            .Should().BeEmpty();

    [Fact]
    public void NonCollectionTruthyValuesEnterTheSectionOnce()
        => Render("{{#note}}[{{.}}]{{/note}}", new Model("x", "here", 0, []))
            .Should().Be("[here]");

    [Fact]
    public void HelperOutputIsEscapedLikeAnInterpolation()
        => Render("| {{upper title}} |", new Model("a | b", null, 0, []))
            .Should().Be(@"| A \| B |");

    [Fact]
    public void ChildScopeFallsBackToTheParent()
        => Render("{{#items}}{{title}}-{{name}};{{/items}}",
                new Model("T", null, 0, [new("a", 1)]))
            .Should().Be("T-a;");

    [Fact]
    public void FormattingIsInvariantByDefaultRegardlessOfAmbientCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            TemplateRenderer.Render(
                    TemplateLexer.Lex("Total: {{total}}"), new Model("x", null, 1234.5m, []))
                .Should().Be("Total: 1234.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ExplicitCultureOptsIntoLocalisedFormatting()
        => TemplateRenderer.Render(
                TemplateLexer.Lex("Total: {{total}}"), new Model("x", null, 1234.5m, []),
                CultureInfo.GetCultureInfo("de-DE"))
            .Should().Be("Total: 1234,5");

    [Fact]
    public void ExplicitCultureReachesHelpers()
        => TemplateRenderer.Render(
                TemplateLexer.Lex("Total: {{format total \"N2\"}}"), new Model("x", null, 1234.5m, []),
                CultureInfo.GetCultureInfo("de-DE"))
            .Should().Be("Total: 1.234,50");

    [Fact]
    public void BoolRendersIdenticallyThroughInterpolationAndHelper()
    {
        TemplateRenderer.Render(TemplateLexer.Lex("{{flag}}"), new FlagModel(true))
            .Should().Be("true");

        TemplateRenderer.Render(TemplateLexer.Lex("{{default flag \"n/a\"}}"), new FlagModel(true))
            .Should().Be("true");
    }
}
