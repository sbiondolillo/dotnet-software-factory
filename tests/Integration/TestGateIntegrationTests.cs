using System.Text.RegularExpressions;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the answers <c>tests/Specs</c> canned for <see cref="TestGate"/> still hold for the real <c>dotnet</c> in a real <see cref="Sandbox"/>.</summary>
public sealed class TestGateIntegrationTests : IDisposable
{
    private const string Tag = "factory-sandbox:testgate-integration";
    private static readonly CommandRunner Runner = new();
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-testgate-tree-");

    public void Dispose() => _tree.Delete(recursive: true);

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution.slnx not found");
    }

    private static string Version(string package)
    {
        string props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Packages.props"));
        return Regex.Match(props, $"Include=\"{Regex.Escape(package)}\" Version=\"([^\"]+)\"").Groups[1].Value;
    }

    private async Task<Sandbox> StartAsync()
    {
        await new SandboxImage(Runner).EnsureAsync(Tag, RepoRoot(), TestContext.Current.CancellationToken);
        return await Sandbox.StartAsync(Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);
    }

    private Task Write(string name, string content) =>
        File.WriteAllTextAsync(Path.Combine(_tree.FullName, name), content, TestContext.Current.CancellationToken);

    private Task WriteTestProject(string code) => Write(
        "Probe.csproj",
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="{Version("Microsoft.NET.Test.Sdk")}" />
            <PackageReference Include="xunit.runner.visualstudio" Version="{Version("xunit.runner.visualstudio")}" />
            <PackageReference Include="xunit.v3" Version="{Version("xunit.v3")}" />
          </ItemGroup>
        </Project>
        """).ContinueWith(_ => Write("Tests.cs", code), TaskScheduler.Default).Unwrap();

    private static Task<CommandResult> Exec(Sandbox sandbox, string[] arguments, string[]? names = null) =>
        sandbox.RunAsync("dotnet", arguments, names ?? [], TestContext.Current.CancellationToken);

    [Fact]
    public async Task FormatCheckExitsWith2AndNamesTheFileWhenAFileNeedsAFormat()
    {
        await Write("Probe.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
        await Write("Greeter.cs", "public class Greeter\n{\npublic void Greet() { }\n}\n");
        await using Sandbox sandbox = await StartAsync();
        CommandResult result = await Exec(sandbox, ["format", "--verify-no-changes", "--no-restore"]);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("WHITESPACE", result.StandardError);
    }

    [Fact]
    public async Task ATestRunWritesTrxOutcomesForPassedFailedAndSkippedTests()
    {
        await WriteTestProject("""
            using Xunit;
            public class ProbeTests
            {
                [Fact] public void Good() { }
                [Fact] public void Bad() => Assert.Equal(1, 2);
                [Fact(Skip = "no")] public void Later() { }
            }
            """);
        await using Sandbox sandbox = await StartAsync();
        Assert.Equal(0, (await Exec(sandbox, ["restore"])).ExitCode);
        CommandResult result = await Exec(sandbox, ["test", "--logger", "trx;LogFileName=probe.trx", "--results-directory", "/work/TestResults/probe"]);
        Assert.Equal(1, result.ExitCode);
        IReadOnlyList<ReportedTest> tests = TestReport.Read(Path.Combine(_tree.FullName, "TestResults", "probe", "probe.trx"));
        Assert.Equal(TestOutcome.Passed, tests.Single(t => t.Method == "Good").Outcome);
        Assert.Equal(TestOutcome.Failed, tests.Single(t => t.Method == "Bad").Outcome);
        Assert.Equal(TestOutcome.Skipped, tests.Single(t => t.Method == "Later").Outcome);
    }

    [Fact]
    public async Task AnUncompilableTestProjectExitsWith1AndWritesNoTrx()
    {
        await WriteTestProject("this does not compile");
        await using Sandbox sandbox = await StartAsync();
        Assert.Equal(0, (await Exec(sandbox, ["restore"])).ExitCode);
        CommandResult result = await Exec(sandbox, ["test", "--no-restore", "--logger", "trx;LogFileName=probe.trx", "--results-directory", "/work/TestResults/probe"]);
        Assert.Equal(1, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_tree.FullName, "TestResults", "probe")));
    }

    [Fact]
    public async Task TheIntegrationCommandExitsWith1ForAFailedTestAnd0ForNoTestAndPassesANameByEnvironment()
    {
        await WriteTestProject("""
            using Xunit;
            public class ProbeTests
            {
                [Fact] public void Bad() => Assert.Equal(1, 2);
            }
            """);
        await using Sandbox sandbox = await StartAsync();
        Assert.Equal(0, (await Exec(sandbox, ["restore"])).ExitCode);
        CommandResult failed = await Exec(sandbox, ["run", "--no-restore"]);
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("Failed: 1", failed.StandardOutput);

        await Write("Tests.cs", "public class Nothing { }\n");
        CommandResult none = await Exec(sandbox, ["run", "--no-restore"]);
        Assert.Equal(0, none.ExitCode);
        Assert.Contains("Total: 0", none.StandardOutput);

        await Write("Tests.cs", """
            using Xunit;
            public class ProbeTests
            {
                [Fact] public void Named() => Assert.NotNull(System.Environment.GetEnvironmentVariable("PROBE_NAME"));
            }
            """);
        string previous = Environment.GetEnvironmentVariable("PROBE_NAME") ?? "";
        Environment.SetEnvironmentVariable("PROBE_NAME", "value");
        try
        {
            Assert.Equal(0, (await Exec(sandbox, ["run", "--no-restore"], ["PROBE_NAME"])).ExitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROBE_NAME", previous.Length is 0 ? null : previous);
        }

        Assert.Equal(1, (await Exec(sandbox, ["run", "--no-restore"])).ExitCode);
    }

    [Fact]
    public async Task LockedRestoreExitsWith1AndNu1004WhenTheLockFileLacksAReference()
    {
        await Write(
            "Probe.csproj",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit.v3" Version="{Version("xunit.v3")}" />
              </ItemGroup>
            </Project>
            """);
        await Write("packages.lock.json", "{\"version\":2,\"dependencies\":{\"net10.0\":{}}}");
        await using Sandbox sandbox = await StartAsync();
        CommandResult result = await Exec(sandbox, ["restore", "--locked-mode"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error NU1004", result.StandardOutput);
    }

    [Fact]
    public async Task AnUnboundScenarioIsAFailedTestInTheTrxFileAndTheTestCommandExitsWith1()
    {
        await Write(
            "Probe.csproj",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="{Version("Microsoft.NET.Test.Sdk")}" />
                <PackageReference Include="Reqnroll.xunit.v3" Version="{Version("Reqnroll.xunit.v3")}" />
                <PackageReference Include="xunit.runner.visualstudio" Version="{Version("xunit.runner.visualstudio")}" />
                <PackageReference Include="xunit.v3" Version="{Version("xunit.v3")}" />
              </ItemGroup>
            </Project>
            """);
        Directory.CreateDirectory(Path.Combine(_tree.FullName, "specs", "features"));
        await Write("specs/features/probe.feature", "Feature: Probe\n\n  Scenario: An unbound scenario\n    Given a step nobody binds\n");
        await using Sandbox sandbox = await StartAsync();
        Assert.Equal(0, (await Exec(sandbox, ["restore"])).ExitCode);
        CommandResult result = await Exec(sandbox, ["test", "--no-restore", "--logger", "trx;LogFileName=probe.trx", "--results-directory", "/work/TestResults/probe"]);
        Assert.Equal(1, result.ExitCode);
        ReportedTest test = Assert.Single(TestReport.Read(Path.Combine(_tree.FullName, "TestResults", "probe", "probe.trx")));
        Assert.EndsWith(".ProbeFeature", test.ClassName);
        Assert.Equal("AnUnboundScenario", test.Method);
        Assert.Equal(TestOutcome.Failed, test.Outcome);
    }
}
