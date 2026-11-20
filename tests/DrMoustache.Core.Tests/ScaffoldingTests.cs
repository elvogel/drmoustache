namespace DrMoustache.Core.Tests;

using System.Reflection;
using System.Runtime.Versioning;
using FluentAssertions;
using Xunit;

public class ScaffoldingTests
{
    [Fact]
    public void SharedBuildPropertiesApplyTheRepoTargets()
        // Directory.Build.props targets net10.0, plus net11.0 on SDK 11; this suite builds
        // for each, so the assembly reports the one it was compiled for.
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<TargetFrameworkAttribute>()!
            .FrameworkName.Should().BeOneOf(".NETCoreApp,Version=v10.0", ".NETCoreApp,Version=v11.0");

    [Fact]
    public void CoreAssemblyIsReferenceable()
    {
        var asm = typeof(DrMoustache.Abstractions.IDataProvider).Assembly;
        asm.GetName().Name.Should().Be("DrMoustache.Abstractions");
    }
}
