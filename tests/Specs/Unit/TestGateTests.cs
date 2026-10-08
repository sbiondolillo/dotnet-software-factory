using Core;
using Xunit;

namespace Specs.Unit;

public sealed class TestGateTests : IDisposable
{
    private readonly TestGateHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private async Task<TestGateResult> RunAsync(params string[] names)
    {
        await _harness.StartAsync(TestContext.Current.CancellationToken);
        return await _harness.RunAsync(names, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AChangedAddedAndRemovedPathFailsBeforeAnyCommandAndEvidenceHoldsEachPath()
    {
        string root = _harness.WorkTree.FullName;
        File.AppendAllText(Path.Combine(root, "specs", "features", "checkout.feature"), "\n# edited\n");
        File.WriteAllText(Path.Combine(root, "specs", "features", "new.feature"), "Feature: New\n");
        File.Delete(Path.Combine(root, "global.json"));

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        Assert.Equal(0, _harness.DockerCommands);
        string all = string.Join("\n", result.Evidence);
        Assert.Contains("specs/features/checkout.feature", all, StringComparison.Ordinal);
        Assert.Contains("specs/features/new.feature", all, StringComparison.Ordinal);
        Assert.Contains("global.json", all, StringComparison.Ordinal);
        Assert.Equal(3, result.Evidence.Count);
    }

    [Theory]
    [InlineData("restore", "restore", 1)]
    [InlineData("build", "build", 1)]
    [InlineData("format", "format", 2)]
    public async Task AFailedCheckIsTheLastCommandAndTheEvidenceHoldsCodeAndBothOutputs(string subcommand, string name, int code)
    {
        _harness.Answers[subcommand] = new CommandResult { ExitCode = code, StandardOutput = "OUT", StandardError = "ERR" };

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        Assert.Equal(subcommand, _harness.Seen[^1].Subcommand);
        string text = Assert.Single(result.Evidence);
        Assert.Contains($"dotnet {name}", text, StringComparison.Ordinal);
        Assert.Contains($"code {code}", text, StringComparison.Ordinal);
        Assert.Contains("OUT", text, StringComparison.Ordinal);
        Assert.Contains("ERR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingTestCommandWithAPassingTrxFileEndsTheGate()
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1, StandardOutput = "OUT", StandardError = "ERR" };

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        Assert.Equal("test", _harness.Seen[^1].Subcommand);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("dotnet test", text, StringComparison.Ordinal);
        Assert.Contains("OUT", text, StringComparison.Ordinal);
        Assert.Contains("ERR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedTestIsNamedWithClassMethodAndMessageAndTheIntegrationCommandDoesNotRun()
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1 };
        _harness.Trx = [TestGateHarness.PassingFeatureTest, ("Specs.Unit.CheckoutTests", "AddsTwoPrices", "Failed", "Assert.Equal() Failure: Values differ")];

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        Assert.DoesNotContain(_harness.Seen, c => c.Subcommand is "run");
        string all = string.Join("\n", result.Evidence);
        Assert.Contains("Specs.Unit.CheckoutTests", all, StringComparison.Ordinal);
        Assert.Contains("AddsTwoPrices", all, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal() Failure: Values differ", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASkippedTestFailsTheGateAndIsNamed()
    {
        _harness.Trx = [TestGateHarness.PassingFeatureTest, ("Specs.Unit.CheckoutTests", "AddsInFrench", "NotExecuted", null)];

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("Specs.Unit.CheckoutTests", text, StringComparison.Ordinal);
        Assert.Contains("AddsInFrench", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScenarioWithNoTestIsNamedWithFileTitleAndTestName()
    {
        _harness.Trx = [TestGateHarness.PassingUnitTest];

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("specs/features/checkout.feature", text, StringComparison.Ordinal);
        Assert.Contains("The total is the sum of the prices", text, StringComparison.Ordinal);
        Assert.Contains("TheTotalIsTheSumOfThePrices", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATitleWithAHyphenBecomesADifferentIdentifierThanTheOneInTheFile()
    {
        using var harness = new TestGateHarness("Feature: F\n\n  Scenario: A per-location total\n    Given x\n");
        harness.Trx = [("Specs.Features.FFeature", "APerLocationTotal", "Passed", null)];
        await harness.StartAsync(TestContext.Current.CancellationToken);

        TestGateResult result = await harness.RunAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.Passed);
        Assert.Contains("APer_LocationTotal", Assert.Single(result.Evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATestWithAPassedAndAFailedResultFailsAndIsNamed()
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1 };
        _harness.Trx =
        [
            TestGateHarness.PassingFeatureTest,
            ("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Failed", "boom"),
        ];

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        Assert.Contains("TheTotalIsTheSumOfThePrices", string.Join("\n", result.Evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATrxFileOfAnEarlierRunIsNotRead()
    {
        _harness.WriteEarlierTrx("Failed");

        TestGateResult result = await RunAsync();

        Assert.True(result.Passed, string.Join("\n", result.Evidence));
    }

    [Fact]
    public async Task AnEarlierTrxFileDoesNotHideATestCommandThatWroteNone()
    {
        _harness.WriteEarlierTrx("Passed");
        _harness.Answers["test"] = new CommandResult { ExitCode = 1, StandardOutput = "reason" };
        _harness.Trx = null;

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("dotnet test", text, StringComparison.Ordinal);
        Assert.Contains("reason", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATestCommandThatExitsZeroAndWritesNoTrxFileFailsForEachScenario()
    {
        _harness.Trx = null;

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("specs/features/checkout.feature", text, StringComparison.Ordinal);
        Assert.Contains("TheTotalIsTheSumOfThePrices", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedIntegrationCommandFailsTheGateWithCodeAndSummary()
    {
        _harness.Answers["run"] = new CommandResult { ExitCode = 1, StandardOutput = "Failed: 1" };

        TestGateResult result = await RunAsync();

        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("dotnet run", text, StringComparison.Ordinal);
        Assert.Contains("code 1", text, StringComparison.Ordinal);
        Assert.Contains("Failed: 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardErrorOnACommandThatExitsZeroDoesNotFailTheGate()
    {
        foreach (string key in _harness.Answers.Keys.ToList())
        {
            _harness.Answers[key] = new CommandResult { StandardError = "An issue was encountered verifying workloads" };
        }

        Assert.True((await RunAsync()).Passed);
    }

    [Fact]
    public async Task SecretNamesReachOnlyTheIntegrationCommand()
    {
        await RunAsync("API_KEY", "SERVICE_TOKEN");

        Assert.Equal(["API_KEY", "SERVICE_TOKEN"], _harness.Seen.Single(c => c.Subcommand is "run").EnvNames);
        Assert.All(_harness.Seen.Where(c => c.Subcommand is not "run"), c => Assert.Empty(c.EnvNames));
    }

    [Fact]
    public async Task EachFeatureFileInASubDirectoryGetsItsOwnCheck()
    {
        using var harness = new TestGateHarness();
        Directory.CreateDirectory(Path.Combine(harness.WorkTree.FullName, "specs", "features", "sub"));
        File.WriteAllText(Path.Combine(harness.WorkTree.FullName, "specs", "features", "sub", "other.feature"), "Feature: Other\n\n  Scenario: Another one\n    Given x\n");
        TestGateResult result = await RunWithNewPin(harness);

        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("specs/features/sub/other.feature", text, StringComparison.Ordinal);
        Assert.Contains("AnotherOne", text, StringComparison.Ordinal);
    }

    private static async Task<TestGateResult> RunWithNewPin(TestGateHarness harness)
    {
        await harness.StartAsync(TestContext.Current.CancellationToken);
        return await new TestGate(harness.Sandbox!).RunAsync(
            harness.WorkTree.FullName, Pin.Take(harness.WorkTree.FullName), [], TestContext.Current.CancellationToken);
    }
}
