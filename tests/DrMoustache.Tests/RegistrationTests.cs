namespace DrMoustache.Tests;

using System.Runtime.CompilerServices;
using DrMoustache.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public class RegistrationTests
{
    [DrMoustacheRecord("station")]
    private sealed record Station
    {
        [Field] public required string Name { get; init; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection.")]
    private sealed class StationProvider : DataProvider<Station>
    {
        public override string Name => "stations";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new Station { Name = "alpha" };
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection.")]
    private sealed class OtherProvider : DataProvider<Station>
    {
        public override string Name => "other";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield break;
        }
    }

    /// <summary>Takes a dependency, to prove providers are constructed by DI.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via dependency injection.")]
    private sealed class InjectedProvider(IOptions<DrMoustacheOptions> options)
        : DataProvider<Station>
    {
        public string SeenOutputDirectory => options.Value.OutputDirectory;

        public override string Name => "injected";

        public override async IAsyncEnumerable<Station> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield break;
        }
    }

    private static ServiceProvider Build(Action<IDrMoustacheBuilder> configure,
        Action<DrMoustacheOptions>? options = null)
    {
        var services = new ServiceCollection();
        configure(services.AddDrMoustache(options));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvesAProviderByItsRegisteredName()
    {
        using var sp = Build(b => b.AddSource<StationProvider>("stations"));

        sp.GetRequiredKeyedService<IDataProvider>("stations")
            .Should().BeOfType<StationProvider>();
    }

    [Fact]
    public void KeepsSeveralSourcesApart()
    {
        using var sp = Build(b => b
            .AddSource<StationProvider>("stations")
            .AddSource<OtherProvider>("other"));

        sp.GetRequiredKeyedService<IDataProvider>("stations").Should().BeOfType<StationProvider>();
        sp.GetRequiredKeyedService<IDataProvider>("other").Should().BeOfType<OtherProvider>();
    }

    [Fact]
    public void RecordsWhatWasRegisteredSoErrorsCanListIt()
    {
        using var sp = Build(b => b
            .AddSource<StationProvider>("stations")
            .AddSource<OtherProvider>("other"));

        sp.GetRequiredService<IEnumerable<SourceRegistration>>()
            .Select(r => r.Name)
            .Should().BeEquivalentTo(["stations", "other"]);
    }

    [Fact]
    public void ProvidersAreConstructedByDependencyInjection()
    {
        using var sp = Build(
            b => b.AddSource<InjectedProvider>("injected"),
            o => o.OutputDirectory = "somewhere");

        sp.GetRequiredKeyedService<IDataProvider>("injected")
            .Should().BeOfType<InjectedProvider>()
            .Which.SeenOutputDirectory.Should().Be("somewhere");
    }

    [Fact]
    public void ProvidersAreSingletons()
    {
        using var sp = Build(b => b.AddSource<StationProvider>("stations"));

        sp.GetRequiredKeyedService<IDataProvider>("stations")
            .Should().BeSameAs(sp.GetRequiredKeyedService<IDataProvider>("stations"));
    }

    [Fact]
    public void OptionsAreResolvableAndCarryTheConfiguredValues()
    {
        using var sp = Build(_ => { }, o => o.Strict = true);

        sp.GetRequiredService<IOptions<DrMoustacheOptions>>()
            .Value.Strict.Should().BeTrue();
    }

    [Fact]
    public void OptionsAreResolvableWithNoConfigureCallback()
    {
        using var sp = Build(_ => { });

        sp.GetRequiredService<IOptions<DrMoustacheOptions>>()
            .Value.OutputDirectory.Should().Be("dist");
    }

    [Fact]
    public void RegisteringTheSameNameTwiceIsRejected()
    {
        var services = new ServiceCollection();
        var builder = services.AddDrMoustache().AddSource<StationProvider>("stations");

        var act = () => builder.AddSource<OtherProvider>("stations");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*stations*already*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankSourceNameIsRejected(string name)
    {
        var services = new ServiceCollection();
        var builder = services.AddDrMoustache();

        var act = () => builder.AddSource<StationProvider>(name);

        act.Should().Throw<ArgumentException>();
    }
}
