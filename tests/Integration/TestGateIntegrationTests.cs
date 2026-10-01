using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the answers <c>tests/Specs</c> canned for the commands <see cref="TestGate"/> runs: each test starts a
/// real container over a work tree it writes, and runs the real program.</summary>
public sealed class TestGateIntegrationTests : IDisposable
{
    private const string Tag = "factory-sandbox:testgate-integration";
    private static readonly CommandRunner Runner = new();
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-testgate-tree-");

    public void Dispose() => _tree.Delete(recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution.slnx not found");
    }

    private static string Project(string assemblyName, string extra = "") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Exe</OutputType>
            <AssemblyName>{assemblyName}</AssemblyName>
            <RootNamespace>{assemblyName}</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
            <PackageReference Include="xunit.v3" Version="3.2.2" />
            {extra}
          </ItemGroup>
        </Project>
        """;

    private async Task WriteAsync(string relative, string content)
    {
        string path = Path.Combine(_tree.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, Token);
    }

    private async Task<Sandbox> StartAsync()
    {
        await new SandboxImage(Runner).EnsureAsync(Tag, RepoRoot(), Token);
        return await Sandbox.StartAsync(Runner, Tag, RepoRoot(), _tree.FullName, Token);
    }

    private static Task<CommandResult> DotnetAsync(Sandbox sandbox, IReadOnlyList<string> arguments, IReadOnlyList<string>? names = null) =>
        sandbox.RunAsync("dotnet", arguments, names ?? [], Token);

    private async Task<(CommandResult Result, IReadOnlyList<ReportedTest> Tests)> TestAsync(string source, string extraPackages = "")
    {
        await WriteAsync("Probe.csproj", Project("Specs", extraPackages));
        await WriteAsync("ProbeTests.cs", source);
        await using Sandbox sandbox = await StartAsync();
        CommandResult result = await DotnetAsync(sandbox, ["test", "--logger", "trx;LogFileName=out.trx", "--results-directory", "/work/out"]);
        string trx = Path.Combine(_tree.FullName, "out", "out.trx");
        return (result, File.Exists(trx) ? TestReport.Read(trx) : []);
    }

    [Fact]
    public async Task RestoreInLockedModeWithAPackageTheLockLacksExitsWithNU1004()
    {
        await WriteAsync("Probe.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        await WriteAsync("packages.lock.json", "{\"version\":1,\"dependencies\":{\"net10.0\":{}}}");
        await using Sandbox sandbox = await StartAsync();

        CommandResult result = await DotnetAsync(sandbox, ["restore", "--locked-mode"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error NU1004", result.StandardOutput);
    }

    [Fact]
    public async Task TheFormatCheckOnAnIndentedMethodExitsWithTwoAndWritesErrorWhitespace()
    {
        await WriteAsync("Probe.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
        await WriteAsync("Greeter.cs", "public class Greeter\n{\npublic void Greet() { }\n}\n");
        await using Sandbox sandbox = await StartAsync();
        CommandResult restore = await DotnetAsync(sandbox, ["restore"]);
        Assert.Equal(0, restore.ExitCode);

        CommandResult result = await DotnetAsync(sandbox, ["format", "--verify-no-changes", "--no-restore"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("error WHITESPACE", result.StandardError);
    }

    [Fact]
    public async Task AFailingTestExitsWithOneAndTheTrxFileHoldsFailedAndTheMessage()
    {
        (CommandResult result, IReadOnlyList<ReportedTest> tests) = await TestAsync(
            "using Xunit;\nnamespace Specs.Unit;\npublic class CheckoutTests\n{\n    [Fact] public void AddsTwoPrices() => Assert.Equal(1, 2);\n}\n");

        Assert.Equal(1, result.ExitCode);
        ReportedTest test = Assert.Single(tests);
        Assert.Equal(TestOutcome.Failed, test.Outcome);
        Assert.Equal("Specs.Unit.CheckoutTests", test.ClassName);
        Assert.Equal("AddsTwoPrices", test.Method);
        Assert.Contains("Assert.Equal() Failure: Values differ", test.Message);
    }

    [Fact]
    public async Task ASkippedTestExitsWithZeroAndTheTrxFileHoldsNotExecuted()
    {
        (CommandResult result, IReadOnlyList<ReportedTest> tests) = await TestAsync(
            "using Xunit;\nnamespace Specs.Unit;\npublic class CheckoutTests\n{\n    [Fact(Skip = \"held back\")] public void AddsInFrench() { }\n}\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(TestOutcome.Skipped, Assert.Single(tests).Outcome);
    }

    [Fact]
    public async Task AnUnboundScenarioExitsWithOneAndTheTrxFileHoldsFailedForItsFeatureClass()
    {
        await WriteAsync("Features/Probe.feature", "Feature: Probe\n\n  Scenario: An unbound scenario\n    Given nothing binds this\n");
        await WriteAsync("reqnroll.json", "{\"trace\":{\"traceSuccessfulSteps\":true}}");
        (CommandResult result, IReadOnlyList<ReportedTest> tests) = await TestAsync(
            "namespace Specs;\npublic class Placeholder { }\n",
            "<PackageReference Include=\"Reqnroll.xunit.v3\" Version=\"3.3.4\" />\n<Content Include=\"Features/Probe.feature\" />");

        Assert.Equal(1, result.ExitCode);
        ReportedTest test = Assert.Single(tests);
        Assert.Equal(TestOutcome.Failed, test.Outcome);
        Assert.Equal("AnUnboundScenario", test.Method);
        Assert.Equal("Specs.Features.ProbeFeature", test.ClassName);
    }

    [Fact]
    public async Task AFileThatDoesNotCompileExitsWithOneWithCS0029AndWritesNoTrxFile()
    {
        (CommandResult result, IReadOnlyList<ReportedTest> tests) = await TestAsync(
            "namespace Specs;\npublic class Broken\n{\n    public int Value = \"text\";\n}\n");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error CS0029", result.StandardOutput);
        Assert.Empty(tests);
    }

    private async Task<CommandResult> IntegrationAsync(string source, IReadOnlyList<string>? names = null)
    {
        await WriteAsync("tests/Integration/Integration.csproj", Project("Integration"));
        await WriteAsync("tests/Integration/ProbeTests.cs", source);
        await using Sandbox sandbox = await StartAsync();
        CommandResult restore = await DotnetAsync(sandbox, ["restore", "tests/Integration"]);
        Assert.Equal(0, restore.ExitCode);
        return await DotnetAsync(sandbox, ["run", "--no-restore", "--project", "tests/Integration"], names);
    }

    [Fact]
    public async Task AnIntegrationProjectWithAFailingTestExitsWithOneAndTheSummaryHoldsFailedOne()
    {
        CommandResult result = await IntegrationAsync(
            "using Xunit;\nnamespace Integration;\npublic class ProbeTests\n{\n    [Fact] public void Fails() => Assert.Equal(1, 2);\n}\n");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Failed: 1", result.StandardOutput);
    }

    [Fact]
    public async Task AnIntegrationProjectWithNoTestExitsWithZeroAndTheSummaryHoldsTotalZero()
    {
        CommandResult result = await IntegrationAsync("namespace Integration;\npublic class Placeholder { }\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Total: 0", result.StandardOutput);
    }

    private const string ProbeNameTest =
        "using Xunit;\nnamespace Integration;\npublic class ProbeTests\n{\n    [Fact] public void NameIsSet() => Assert.False(string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(\"PROBE_NAME\")));\n}\n";

    [Fact]
    public async Task ANameGivenWithEnvReachesTheIntegrationTest()
    {
        Environment.SetEnvironmentVariable("PROBE_NAME", "probe-value");

        CommandResult result = await IntegrationAsync(ProbeNameTest, ["PROBE_NAME"]);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task WithoutTheNameTheIntegrationTestFails()
    {
        Environment.SetEnvironmentVariable("PROBE_NAME", "probe-value");

        CommandResult result = await IntegrationAsync(ProbeNameTest);

        Assert.Equal(1, result.ExitCode);
    }
}
