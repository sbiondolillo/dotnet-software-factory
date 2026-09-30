using System.Xml.Linq;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the answers <c>tests/Specs</c> canned for <see cref="TestGate"/> still hold on a real container.</summary>
public sealed class TestGateIntegrationTests : IDisposable
{
    private const string Tag = "factory-sandbox:testgate-integration";
    private const string Passing = "    [Fact]\n    public void Passes()\n    {\n        Assert.Equal(2, 1 + 1);\n    }\n";
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

    private static string Version(string package) =>
        XDocument.Load(Path.Combine(RepoRoot(), "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Single(element => (string?)element.Attribute("Include") == package)
            .Attribute("Version")!.Value;

    private static string Reference(string package) => $"    <PackageReference Include=\"{package}\" Version=\"{Version(package)}\" />\n";

    private static string Wrap(string ns, string className, string body) =>
        $"using Xunit;\n\nnamespace {ns};\n\npublic class {className}\n{{\n{body}}}\n";

    private void Write(string name, string content)
    {
        string path = Path.Combine(_tree.FullName, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Writes a probe work tree: a real xunit.v3 test project and an integration project, each with the package references at the versions of Directory.Packages.props.</summary>
    private void Build(
        string unitBody = Passing,
        string integrationBody = Passing,
        bool unboundScenario = false,
        bool unlockedCorePackage = false,
        Dictionary<string, string>? extras = null)
    {
        File.Copy(Path.Combine(RepoRoot(), "global.json"), Path.Combine(_tree.FullName, "global.json"));
        string reqnroll = unboundScenario ? Reference("Reqnroll.xunit.v3") : "";
        Write(
            "tests/Specs/Specs.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n"
            + "    <IsPackable>false</IsPackable>\n    <IsTestProject>true</IsTestProject>\n    <ReqnrollUseIntermediateOutputPathForCodeBehind>true</ReqnrollUseIntermediateOutputPathForCodeBehind>\n  </PropertyGroup>\n  <ItemGroup>\n"
            + Reference("Microsoft.NET.Test.Sdk") + Reference("xunit.runner.visualstudio") + Reference("xunit.v3") + reqnroll
            + "  </ItemGroup>\n  <ItemGroup>\n    <ReqnrollFeatureFiles Include=\"../../specs/features/**/*.feature\" Link=\"Features/%(RecursiveDir)%(Filename)%(Extension)\" />\n  </ItemGroup>\n</Project>\n");
        Write("tests/Specs/Unit/ProbeTests.cs", Wrap("Specs.Unit", "ProbeTests", unitBody));
        Write("tests/Specs/Greeter.cs", "namespace Specs;\n\npublic static class Greeter\n{\n    public static string Greet() => \"hi\";\n}\n");
        Write(
            "tests/Integration/Integration.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n"
            + "    <OutputType>Exe</OutputType>\n    <IsPackable>false</IsPackable>\n    <IsTestProject>false</IsTestProject>\n  </PropertyGroup>\n  <ItemGroup>\n"
            + Reference("xunit.v3") + "  </ItemGroup>\n</Project>\n");
        Write("tests/Integration/ProbeIntegrationTests.cs", Wrap("Integration", "ProbeIntegrationTests", integrationBody));
        string projects = "  <Project Path=\"tests/Specs/Specs.csproj\" />\n  <Project Path=\"tests/Integration/Integration.csproj\" />\n";
        if (unboundScenario)
        {
            Write("tests/Specs/reqnroll.json", "{\n  \"generator\": {\n    \"disableFriendlyTestNames\": true\n  }\n}\n");
            Write("specs/features/probe.feature", "Feature: Probe\n\n  Scenario: An unbound scenario\n    Given a step nobody binds\n");
        }

        if (unlockedCorePackage)
        {
            Write(
                "src/Core/Core.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n"
                + Reference("Gherkin") + "  </ItemGroup>\n</Project>\n");
            Write("src/Core/packages.lock.json", "{\n  \"version\": 1,\n  \"dependencies\": {\n    \"net10.0\": {}\n  }\n}\n");
            projects += "  <Project Path=\"src/Core/Core.csproj\" />\n";
        }

        Write("Solution.slnx", "<Solution>\n" + projects + "</Solution>\n");
        foreach ((string name, string content) in extras ?? [])
        {
            Write(name, content);
        }
    }

    private async Task<string> RunAsync(bool expectPass = false, string[]? names = null)
    {
        Pin pin = Pin.Take(_tree.FullName);
        await new SandboxImage(Runner).EnsureAsync(Tag, RepoRoot(), TestContext.Current.CancellationToken);
        await using Sandbox sandbox = await Sandbox.StartAsync(Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);
        TestGateResult result = await new TestGate(sandbox).RunAsync(_tree.FullName, pin, names ?? [], TestContext.Current.CancellationToken);
        string evidence = string.Join("\n", result.Evidence);
        Assert.True(result.Passed == expectPass, evidence);
        return evidence;
    }

    [Fact]
    public async Task ARestoredTreeWithPassingTestsPasses()
    {
        Build();
        Assert.Equal("", await RunAsync(expectPass: true));
    }

    [Fact]
    public async Task ACorePackageTheLockLacksGivesNu1004InTheRestoreEvidence()
    {
        Build(unlockedCorePackage: true);
        string evidence = await RunAsync();
        Assert.Contains("dotnet restore", evidence, StringComparison.Ordinal);
        Assert.Contains("error NU1004", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABuildErrorGivesOneTextWithTheCompilerErrorAndNoTrx()
    {
        Build(extras: new() { ["tests/Specs/Broken.cs"] = "class Broken { int X = \"x\"; }\n" });
        string evidence = await RunAsync();
        Assert.Contains("dotnet build", evidence, StringComparison.Ordinal);
        Assert.Contains("error CS0029", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("TRX", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEightSpaceFileGivesTheWhitespaceErrorOfTheFormatCheck()
    {
        Build(extras: new() { ["tests/Specs/Greeter.cs"] = "namespace Specs;\n\npublic static class Greeter\n{\n        public static string Greet() => \"hi\";\n}\n" });
        string evidence = await RunAsync();
        Assert.Contains("dotnet format", evidence, StringComparison.Ordinal);
        Assert.Contains("error WHITESPACE", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingProbeTestGivesItsClassMethodAndMessage()
    {
        Build(unitBody: "    [Fact]\n    public void AddsTwoPrices()\n    {\n        Assert.Equal(1, 2);\n    }\n");
        string evidence = await RunAsync();
        Assert.Contains("Specs.Unit.ProbeTests", evidence, StringComparison.Ordinal);
        Assert.Contains("AddsTwoPrices", evidence, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal() Failure: Values differ", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASkippedTestGivesASkippedTestText()
    {
        Build(unitBody: Passing + "\n    [Fact(Skip = \"held back\")]\n    public void AddsInFrench()\n    {\n        Assert.Equal(2, 1 + 1);\n    }\n");
        string evidence = await RunAsync();
        Assert.Contains("skipped test", evidence, StringComparison.Ordinal);
        Assert.Contains("Specs.Unit.ProbeTests", evidence, StringComparison.Ordinal);
        Assert.Contains("AddsInFrench", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnboundScenarioGivesAFailedTextForItsGeneratedTest()
    {
        Build(unboundScenario: true);
        string evidence = await RunAsync();
        Assert.Contains("failed test", evidence, StringComparison.Ordinal);
        Assert.Contains("Specs.Features.ProbeFeature", evidence, StringComparison.Ordinal);
        Assert.Contains("AnUnboundScenario", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingIntegrationTestGivesItsSummary()
    {
        Build(integrationBody: "    [Fact]\n    public void Fails()\n    {\n        Assert.Equal(1, 2);\n    }\n");
        string evidence = await RunAsync();
        Assert.Contains("dotnet run", evidence, StringComparison.Ordinal);
        Assert.Contains("Failed: 1", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIntegrationProjectWithNoTestPassesWithTotalZero()
    {
        Build(integrationBody: "");
        Assert.Equal("", await RunAsync(expectPass: true));
    }

    [Fact]
    public async Task ATestReadingProbeNamePassesWithTheNameGivenAndFailsWithout()
    {
        const string Body = "    [Fact]\n    public void ReadsTheVariable()\n    {\n        Assert.Equal(\"probe-value\", Environment.GetEnvironmentVariable(\"PROBE_NAME\"));\n    }\n";
        Environment.SetEnvironmentVariable("PROBE_NAME", "probe-value");
        try
        {
            Build(integrationBody: Body);
            Assert.Equal("", await RunAsync(expectPass: true, names: ["PROBE_NAME"]));

            string evidence = await RunAsync();
            Assert.Contains("Failed: 1", evidence, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROBE_NAME", null);
        }
    }
}
