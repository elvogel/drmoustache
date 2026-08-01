namespace DrMoustache.Core.Tests;

using System.Reflection;
using System.Runtime.Versioning;
using FluentAssertions;
using Xunit;

public class ScaffoldingTests
{
    [Fact]
    public void SharedBuildPropertiesApplyNet10()
        => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<TargetFrameworkAttribute>()!
            .FrameworkName.Should().Be(".NETCoreApp,Version=v10.0");
}
