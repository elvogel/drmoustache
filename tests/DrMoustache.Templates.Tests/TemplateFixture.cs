namespace DrMoustache.Templates.Tests;

using System.Diagnostics;
using Xunit;

/// <summary>Groups <see cref="MinimalTemplateTests"/> and
/// <see cref="SampleTemplateTests"/> so they share one
/// <see cref="TemplateFixture"/> instead of each building and installing
/// their own. Sharing halves the pack/install cost and, because xUnit runs
/// tests within a single collection sequentially, keeps the two classes'
/// use of the machine-global template engine store from racing.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1515:Consider making public types internal",
    Justification = "Must be public: xUnit1027 requires collection definition classes public.")]
[CollectionDefinition(Name)]
public sealed class TemplateCollectionDefinition : ICollectionFixture<TemplateFixture>
{
    public const string Name = "templates";
}

/// <summary>Packs the solution to a local feed and installs the template
/// package once for the whole test class.</summary>
/// <remarks>Must stay public: xUnit's <c>IClassFixture&lt;T&gt;</c> requires the
/// public test classes in this assembly to name this type in their public
/// constructor and base-interface list (xUnit1000 forbids internal test
/// classes), so CA1515's internal-izable suggestion does not apply here.</remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1515:Consider making public types internal",
    Justification = "Must be public for xUnit's IClassFixture<T> contract; see type remarks.")]
public sealed class TemplateFixture : IDisposable
{
    private const string PackageId = "DrMoustache.Templates";

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public string Feed { get; }
    public string WorkRoot { get; }

    public TemplateFixture()
    {
        Feed = Directory.CreateTempSubdirectory("drm-feed-").FullName;
        WorkRoot = Directory.CreateTempSubdirectory("drm-work-").FullName;

        Run("dotnet", ["pack", Path.Combine(RepositoryRoot, "DrMoustache.slnx"),
            "-c", "Release", "-o", Feed, "--nologo"], RepositoryRoot);

        var nupkg = Directory.EnumerateFiles(Feed, $"{PackageId}.*.nupkg").Single();

        // Uninstall first so a leftover install from a previous run, or from
        // the developer's own machine, cannot mask a broken package.
        RunAllowingFailure("dotnet", ["new", "uninstall", PackageId], RepositoryRoot);
        Run("dotnet", ["new", "install", nupkg], RepositoryRoot);
    }

    /// <summary>Instantiates a template and points it at the local feed.</summary>
    /// <returns>The instantiated project directory.</returns>
    public string Instantiate(string shortName, string projectName)
    {
        var into = Path.Combine(WorkRoot, projectName);
        Directory.CreateDirectory(into);

        Run("dotnet", ["new", shortName, "-n", projectName, "-o", into], WorkRoot);

        File.WriteAllText(Path.Combine(into, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{Feed}" />
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        return into;
    }

    /// <summary>Escapes braces so captured process output is safe to pass as a
    /// FluentAssertions "because" argument.</summary>
    /// <remarks>FluentAssertions formats "because" as a format string, so an
    /// unescaped brace in compiler or MSBuild output throws FormatException
    /// instead of reporting the assertion failure — hiding the very diagnostic
    /// the caller needs.</remarks>
    public static string Diagnostic(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        return output.Replace("{", "{{", StringComparison.Ordinal)
                      .Replace("}", "}}", StringComparison.Ordinal);
    }

    public static (int ExitCode, string Output) TryRun(
        string file, string[] arguments, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var info = new ProcessStartInfo
        {
            FileName = file,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {file}");

        // Both pipes must be drained concurrently. Reading stdout to the end
        // first deadlocks as soon as the child fills the ~64KB stderr buffer
        // while we are still blocked on stdout — and the children here are
        // dotnet pack/build/run over deliberately failing scaffolds, which is
        // precisely when the output is largest. It would present as CI hanging
        // until the job timeout with nothing to show for it.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        var streams = Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();

        process.WaitForExit();

        return (process.ExitCode, streams[0] + streams[1]);
    }

    private static void Run(string file, string[] arguments, string workingDirectory)
    {
        var (exitCode, output) = TryRun(file, arguments, workingDirectory);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"{file} {string.Join(' ', arguments)} failed ({exitCode}):\n{output}");
        }
    }

    private static void RunAllowingFailure(
        string file, string[] arguments, string workingDirectory)
        => TryRun(file, arguments, workingDirectory);

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DrMoustache.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("tests must run inside the repository");
    }

    public void Dispose()
    {
        RunAllowingFailure("dotnet", ["new", "uninstall", PackageId], WorkRoot);

        foreach (var d in new[] { Feed, WorkRoot })
        {
            try { Directory.Delete(d, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }
}
