using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the TRX text <c>tests/Specs</c> canned is what the real TRX logger writes.</summary>
public sealed class TestReportIntegrationTests
{
    [Fact]
    public async Task TheRealTrxLoggerFileReadsAsPassedFailedAndSkipped()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-trx-integration-");
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Sample.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit.v3" Version="3.2.2" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
              </ItemGroup>
            </Project>
            """, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "SampleTests.cs"), """
            using Xunit;
            namespace Sample;
            public class SampleTests
            {
                [Fact] public void Passes() { }
                [Fact] public void Fails() => Assert.Equal(1, 2);
                [Fact(Skip = "held back")] public void Skipped() { }
            }
            """, cancellationToken);

        var runner = new CommandRunner();
        CommandResult result = await runner.RunAsync(new CommandSpec
        {
            Command = "dotnet",
            Arguments = ["test", "--logger", "trx;LogFileName=out.trx", "--results-directory", directory.FullName],
            WorkingDirectory = directory.FullName,
            Timeout = TimeSpan.FromMinutes(5),
        }, cancellationToken);
        _ = result;

        IReadOnlyList<ReportedTest> tests = TestReport.Read(Path.Combine(directory.FullName, "out.trx"));
        Assert.Equal(3, tests.Count);
        Assert.All(tests, test => Assert.Equal("Sample.SampleTests", test.ClassName));
        Assert.Equal(TestOutcome.Passed, tests.Single(test => test.Method == "Passes").Outcome);
        ReportedTest failed = tests.Single(test => test.Method == "Fails");
        Assert.Equal(TestOutcome.Failed, failed.Outcome);
        Assert.Contains("Assert.Equal() Failure", failed.Message);
        Assert.DoesNotContain("   at ", failed.Message);
        Assert.Equal(TestOutcome.Skipped, tests.Single(test => test.Method == "Skipped").Outcome);
    }
}
