using Core;
using Xunit;

namespace Specs.Unit;

public sealed class TestGateTests : IDisposable
{
    private readonly TestGateHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private async Task<GateOutcome> RunAsync(params string[] names)
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

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        Assert.Equal(0, _harness.DockerCommands);
        string all = string.Join("\n", result.RedoTexts);
        Assert.Contains("specs/features/checkout.feature", all, StringComparison.Ordinal);
        Assert.Contains("specs/features/new.feature", all, StringComparison.Ordinal);
        Assert.Contains("global.json", all, StringComparison.Ordinal);
        Assert.Equal(3, result.RedoTexts.Count);
    }

    [Theory]
    [InlineData("restore", "restore", 1)]
    [InlineData("build", "build", 1)]
    [InlineData("format", "format", 2)]
    public async Task AFailedCheckIsTheLastCommandAndTheEvidenceHoldsCodeAndBothOutputs(string subcommand, string name, int code)
    {
        _harness.Answers[subcommand] = new CommandResult { ExitCode = code, StandardOutput = "OUT", StandardError = "ERR" };

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        Assert.Equal(subcommand, _harness.Seen[^1].Subcommand);
        string text = Assert.Single(result.RedoTexts);
        Assert.Contains($"dotnet {name}", text, StringComparison.Ordinal);
        Assert.Contains($"code {code}", text, StringComparison.Ordinal);
        Assert.Contains("OUT", text, StringComparison.Ordinal);
        Assert.Contains("ERR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingTestCommandWithAPassingTrxFileEndsTheGate()
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1, StandardOutput = "OUT", StandardError = "ERR" };

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        Assert.Equal("test", _harness.Seen[^1].Subcommand);
        string text = Assert.Single(result.RedoTexts);
        Assert.Contains("dotnet test", text, StringComparison.Ordinal);
        Assert.Contains("OUT", text, StringComparison.Ordinal);
        Assert.Contains("ERR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedTestIsNamedWithClassMethodAndMessageAndTheIntegrationCommandDoesNotRun()
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1 };
        _harness.Trx = [TestGateHarness.PassingFeatureTest, ("Specs.Unit.CheckoutTests", "AddsTwoPrices", "Failed", "Assert.Equal() Failure: Values differ")];

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        Assert.DoesNotContain(_harness.Seen, c => c.Subcommand is "run");
        string all = string.Join("\n", result.RedoTexts);
        Assert.Contains("Specs.Unit.CheckoutTests", all, StringComparison.Ordinal);
        Assert.Contains("AddsTwoPrices", all, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal() Failure: Values differ", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASkippedTestFailsTheGateAndIsNamed()
    {
        _harness.Trx = [TestGateHarness.PassingFeatureTest, ("Specs.Unit.CheckoutTests", "AddsInFrench", "NotExecuted", null)];

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        string text = Assert.Single(result.RedoTexts);
        Assert.Contains("Specs.Unit.CheckoutTests", text, StringComparison.Ordinal);
        Assert.Contains("AddsInFrench", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScenarioWithNoTestIsNamedWithFileTitleAndTestName()
    {
        _harness.Trx = [TestGateHarness.PassingUnitTest];

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        string text = Assert.Single(result.RedoTexts);
        Assert.Contains("specs/features/checkout.feature", text, StringComparison.Ordinal);
        Assert.Contains("The total is the sum of the prices", text, StringComparison.Ordinal);
        Assert.Contains("TheTotalIsTheSumOfThePrices", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureFileThatDoesNotParseFailsTheGateWithItsPathAndNoTextOfTheFile()
    {
        using var harness = new TestGateHarness("FACTORY_GITHUB_TOKEN=s3cret\n");
        await harness.StartAsync(TestContext.Current.CancellationToken);

        GateOutcome result = await harness.RunAsync([], TestContext.Current.CancellationToken);

        Assert.Equal("failed", result.Result);
        string text = Assert.Single(result.RedoTexts);
        Assert.StartsWith("feature file: specs/features/checkout.feature: ", text, StringComparison.Ordinal);
        Assert.Contains("(1:1)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATitleWithAHyphenBecomesADifferentIdentifierThanTheOneInTheFile()
    {
        using var harness = new TestGateHarness("Feature: F\n\n  Scenario: A per-location total\n    Given x\n");
        harness.Trx = [("Specs.Features.FFeature", "APerLocationTotal", "Passed", null)];
        await harness.StartAsync(TestContext.Current.CancellationToken);

        GateOutcome result = await harness.RunAsync([], TestContext.Current.CancellationToken);

        Assert.Equal("failed", result.Result);
        Assert.Contains("APer_LocationTotal", Assert.Single(result.RedoTexts), StringComparison.Ordinal);
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

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        Assert.Contains("TheTotalIsTheSumOfThePrices", string.Join("\n", result.RedoTexts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATrxFileOfAnEarlierRunIsNotRead()
    {
        _harness.WriteEarlierTrx("Failed");

        GateOutcome result = await RunAsync();

        Assert.True(result.Result is "passed", string.Join("\n", result.RedoTexts));
    }

    [Fact]
    public async Task AnEarlierTrxFileDoesNotHideATestCommandThatWroteNone()
    {
        _harness.WriteEarlierTrx("Passed");
        _harness.Answers["test"] = new CommandResult { ExitCode = 1, StandardOutput = "reason" };
        _harness.Trx = null;

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        string text = Assert.Single(result.RedoTexts);
        Assert.Contains("dotnet test", text, StringComparison.Ordinal);
        Assert.Contains("reason", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATestCommandThatExitsZeroAndWritesNoTrxFileFailsForEachScenario()
    {
        _harness.Trx = null;

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        string text = Assert.Single(result.RedoTexts);
        Assert.Contains("specs/features/checkout.feature", text, StringComparison.Ordinal);
        Assert.Contains("TheTotalIsTheSumOfThePrices", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedIntegrationCommandFailsTheGateWithCodeAndSummary()
    {
        _harness.Answers["run"] = new CommandResult { ExitCode = 1, StandardOutput = "Failed: 1" };

        GateOutcome result = await RunAsync();

        Assert.Equal("failed", result.Result);
        string text = Assert.Single(result.RedoTexts);
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

        Assert.Equal("passed", (await RunAsync()).Result);
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
        GateOutcome result = await RunWithNewPin(harness);

        Assert.Equal("failed", result.Result);
        string text = Assert.Single(result.RedoTexts);
        Assert.Contains("specs/features/sub/other.feature", text, StringComparison.Ordinal);
        Assert.Contains("AnotherOne", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGateThatSkipsTheIntegrationSuiteFailsOnAFailedTestOfTheTrxFile()
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1 };
        _harness.Trx = [TestGateHarness.PassingFeatureTest, ("Specs.Unit.CheckoutTests", "AddsTwoPrices", "Failed", "Assert.Equal() Failure: Values differ")];
        await _harness.StartAsync(TestContext.Current.CancellationToken);

        GateOutcome result = await _harness.RunAsync([], true, TestContext.Current.CancellationToken);

        Assert.Equal("failed", result.Result);
        Assert.Contains(result.RedoTexts, e => e.Contains("Specs.Unit.CheckoutTests.AddsTwoPrices", StringComparison.Ordinal));
        Assert.DoesNotContain(_harness.Seen, c => c.Subcommand is "run");
    }

    [Fact]
    public async Task AGateThatSkipsTheIntegrationSuiteGivesBackTheTestsOfTheTrxFile()
    {
        await _harness.StartAsync(TestContext.Current.CancellationToken);

        GateOutcome result = await _harness.RunAsync([], true, TestContext.Current.CancellationToken);

        Assert.Equal("passed", result.Result);
        Assert.NotNull(_harness.Gate!.Tests);
        Assert.Equal(
            ["Specs.Features.CheckoutFeature.TheTotalIsTheSumOfThePrices", "Specs.Unit.CheckoutTests.AddsTwoPrices"],
            _harness.Gate!.Tests.Select(t => $"{t.ClassName}.{t.Method}"));
    }

    [Fact]
    public async Task TheGateIsNamedTest()
    {
        await _harness.StartAsync(TestContext.Current.CancellationToken);
        var gate = new TestGate(_harness.Sandbox!, _harness.WorkTree.FullName, new FeatureFiles(_harness.WorkTree.FullName), _harness.Pin, []);
        Assert.Equal(GateName.Test, GateSandbox.NameOf(gate));
    }

    [Fact]
    public async Task ATestOfTheBaselineThatNoLongerPassesIsALostTestUnderPassed()
    {
        await _harness.StartAsync(TestContext.Current.CancellationToken);
        var basePassed = new HashSet<string>(StringComparer.Ordinal) { "Specs.Unit.CheckoutTests.AddsTwoPrices", "Specs.Unit.GreeterTests.Greets" };

        GateOutcome result = await _harness.RunAsync(
            new TestGate(_harness.Sandbox!, _harness.WorkTree.FullName, new FeatureFiles(_harness.WorkTree.FullName), _harness.Pin, [], basePassed: basePassed),
            TestContext.Current.CancellationToken);

        Assert.Equal("passed", result.Result);
        Assert.Equal(["lost test: Specs.Unit.GreeterTests.Greets"], result.RedoTexts);
    }

    [Fact]
    public async Task TheFactoryWithNoScopeRunsNoCommandAndChecksNoLostTest()
    {
        await _harness.StartAsync(TestContext.Current.CancellationToken);
        TestGate gate = await TestGate.CreateAsync(
            _harness.Sandbox!, _harness.WorkTree.FullName, new FeatureFiles(_harness.WorkTree.FullName), _harness.Pin, [], null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(_harness.Seen);

        _harness.Trx = [TestGateHarness.PassingFeatureTest];
        GateOutcome result = await _harness.RunAsync(gate, TestContext.Current.CancellationToken);

        Assert.Equal("passed", result.Result);
        Assert.Empty(result.RedoTexts);
    }

    [Fact]
    public async Task TheResultsDirectoryOfTheBaselineAndOfTheGateIsRemovedFromTheWorkTree()
    {
        await _harness.StartAsync(TestContext.Current.CancellationToken);
        TestGate gate = await TestGate.CreateAsync(
            _harness.Sandbox!, _harness.WorkTree.FullName, new FeatureFiles(_harness.WorkTree.FullName), _harness.Pin, [], ["README.md"], cancellationToken: TestContext.Current.CancellationToken);
        await _harness.RunAsync(gate, TestContext.Current.CancellationToken);

        Assert.Equal(2, _harness.Seen.Count(c => c.Subcommand is "test"));
        Assert.Empty(_harness.WorkTree.EnumerateDirectories(".test-*"));
    }

    [Fact]
    public async Task AReadOfTheTrxFileThatThrowsStillRemovesTheResultsDirectory()
    {
        _harness.BrokenTrx = true;

        await Assert.ThrowsAsync<TestReportException>(() => RunAsync());

        Assert.Single(_harness.Seen, c => c.Subcommand is "test");
        Assert.Empty(_harness.WorkTree.EnumerateDirectories(".test-*"));
    }

    [Fact]
    public async Task TheFactoryWithAScopeReadsTheBaselineFromTheTestsOfTheBase()
    {
        await _harness.StartAsync(TestContext.Current.CancellationToken);
        TestGate gate = await TestGate.CreateAsync(
            _harness.Sandbox!, _harness.WorkTree.FullName, new FeatureFiles(_harness.WorkTree.FullName), _harness.Pin, [], ["README.md"], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["restore", "test"], _harness.Seen.Select(c => c.Subcommand));

        _harness.Trx = [TestGateHarness.PassingFeatureTest];
        GateOutcome result = await _harness.RunAsync(gate, TestContext.Current.CancellationToken);

        Assert.Equal("passed", result.Result);
        Assert.Equal(["lost test: Specs.Unit.CheckoutTests.AddsTwoPrices"], result.RedoTexts);
    }

    [Fact]
    public async Task TheFactoryWithAScopeExemptsAPinnedPathInTheScope()
    {
        await _harness.StartAsync(TestContext.Current.CancellationToken);
        TestGate gate = await TestGate.CreateAsync(
            _harness.Sandbox!, _harness.WorkTree.FullName, new FeatureFiles(_harness.WorkTree.FullName), _harness.Pin, [], ["global.json"], cancellationToken: TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(_harness.WorkTree.FullName, "global.json"), """{"sdk":{"version":"10.0.101"}}""");

        GateOutcome result = await _harness.RunAsync(gate, TestContext.Current.CancellationToken);

        Assert.Equal("passed", result.Result);
        Assert.Empty(result.RedoTexts);
    }

    [Fact]
    public async Task AFailedGateIsFailedAndChecksNoLostTest()
    {
        _harness.Answers["build"] = new CommandResult { ExitCode = 1 };
        await _harness.StartAsync(TestContext.Current.CancellationToken);
        var basePassed = new HashSet<string>(StringComparer.Ordinal) { "Specs.Unit.GreeterTests.Greets" };

        GateOutcome result = await _harness.RunAsync(
            new TestGate(_harness.Sandbox!, _harness.WorkTree.FullName, new FeatureFiles(_harness.WorkTree.FullName), _harness.Pin, [], basePassed: basePassed),
            TestContext.Current.CancellationToken);

        Assert.Equal("failed", result.Result);
        Assert.DoesNotContain(result.RedoTexts, text => text.StartsWith("lost test:", StringComparison.Ordinal));
    }

    private static async Task<GateOutcome> RunWithNewPin(TestGateHarness harness)
    {
        await harness.StartAsync(TestContext.Current.CancellationToken);
        return await harness.RunAsync(
            new TestGate(harness.Sandbox!, harness.WorkTree.FullName, new FeatureFiles(harness.WorkTree.FullName), Pin.Take(harness.WorkTree.FullName), []), TestContext.Current.CancellationToken);
    }
}
