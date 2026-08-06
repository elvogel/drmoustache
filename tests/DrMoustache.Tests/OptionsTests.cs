namespace DrMoustache.Tests;

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

public class OptionsTests
{
    [Fact]
    public void DefaultsAreUsableWithNoConfiguration()
    {
        var options = new DrMoustacheOptions();

        options.TemplateDirectory.Should().Be("templates");
        options.OutputDirectory.Should().Be("dist");
        options.Strict.Should().BeFalse();
        options.Culture.Should().BeNull("null means invariant");
    }

    [Fact]
    public void BindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DrMoustache:TemplateDirectory"] = "pages",
                ["DrMoustache:OutputDirectory"] = "out",
                ["DrMoustache:Strict"] = "true",
            })
            .Build();

        var options = new DrMoustacheOptions();
        configuration.GetSection("DrMoustache").Bind(options);

        options.TemplateDirectory.Should().Be("pages");
        options.OutputDirectory.Should().Be("out");
        options.Strict.Should().BeTrue();
    }

    [Fact]
    public void CultureIsSettableForDeliberateLocalisation()
    {
        var options = new DrMoustacheOptions
        {
            Culture = CultureInfo.GetCultureInfo("de-DE"),
        };

        options.Culture!.Name.Should().Be("de-DE");
    }
}
