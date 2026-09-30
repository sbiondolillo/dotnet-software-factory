using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class TestGateSteps : IDisposable
{
    private const string Out = "reason on standard output";
    private const string Err = "reason on standard error";
    private readonly TestGateHarness _harness = new();
    private TestGateResult? _result;
    private bool _started;

    public void Dispose() => _harness.Dispose();

    [Given("a work tree with {string} pinned")]
    public void GivenAWorkTree(string path) => Assert.True(File.Exists(Path.Combine(_harness.WorkTree.FullName, path)));

    [Given("the sandbox answers every docker command from a mock")]
    public void GivenMock() => _started = false;

    [Given("a passing run")]
    public static void GivenPassing()
    {
    }

    [Given("the pin finds {string} changed and {string} removed")]
    public void GivenPinFinds(string changed, string removed)
    {
        File.AppendAllText(Path.Combine(_harness.WorkTree.FullName, changed), "\n# edited\n");
        File.Delete(Path.Combine(_harness.WorkTree.FullName, removed));
    }

    [Given("the restore exits with a code other than 0 and writes a reason to both outputs")]
    public void GivenRestoreFails() => Fail("restore");

    [Given("the build exits with a code other than 0 and writes a reason to both outputs")]
    public void GivenBuildFails() => Fail("build");

    [Given("the format check exits with a code other than 0 and writes a reason to both outputs")]
    public void GivenFormatFails() => Fail("format");

    [Given("the test command exits with the code 1 and writes a reason to both outputs")]
    public void GivenTestFails() => Fail("test");

    [Given("each of the four commands writes a line to standard error")]
    public void GivenStderr()
    {
        foreach (string sub in new[] { "restore", "build", "format", "test" })
        {
            _harness.Answers[sub] = new CommandResult { StandardError = "An issue was encountered verifying workloads" };
        }
    }

    [Given("the test command exits with the code 1 and writes a TRX file with the failed test {string} of the class {string} and the message {string}")]
    public void GivenFailedTrx(string method, string className, string message)
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1 };
        _harness.Trx = [(className, method, "Failed", message)];
    }

    [Given("the test command exits with the code 0 and writes a passing TRX file that also holds the skipped test {string} of the class {string}")]
    public void GivenSkipped(string method, string className) =>
        _harness.Trx = [TestGateHarness.PassingFeatureTest, TestGateHarness.PassingUnitTest, (className, method, "NotExecuted", null)];

    [Given("the test command exits with the code 0 and writes a TRX file with only the passed test {string} of the class {string}")]
    public void GivenOnlyPassed(string method, string className) => _harness.Trx = [(className, method, "Passed", null)];

    [Given("the test command exits with the code 1 and writes a TRX file with two results of the test {string} of the class {string}, one passed and one failed")]
    public void GivenTwoResults(string method, string className)
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1 };
        _harness.Trx = [(className, method, "Passed", null), (className, method, "Failed", "second result failed")];
    }

    [Given("the work tree holds the TRX file of an earlier run with a failed test")]
    public void GivenEarlierFailed() => _harness.WriteEarlierTrx("Failed");

    [Given("the work tree holds the TRX file of an earlier run with a passed test")]
    public void GivenEarlierPassed() => _harness.WriteEarlierTrx("Passed");

    [Given("the test command of this run exits with the code 0 and writes a passing TRX file")]
    public static void GivenThisRunPasses()
    {
    }

    [Given("the test command of this run exits with the code 1, writes a reason to standard output and writes no TRX file")]
    public void GivenNoTrx()
    {
        _harness.Answers["test"] = new CommandResult { ExitCode = 1, StandardOutput = Out };
        _harness.Trx = null;
    }

    [Given("the integration command exits with the code 1 and writes a summary to standard output")]
    public void GivenIntegrationFails() =>
        _harness.Answers["run"] = new CommandResult { ExitCode = 1, StandardOutput = "Integration  Total: 3, Errors: 0, Failed: 1" };

    [When("a caller runs the test gate")]
    public Task WhenRuns() => Run([]);

    [When("a caller runs the test gate with the names {string} and {string}")]
    public Task WhenRunsWithNames(string first, string second) => Run([first, second]);

    [Then("the test gate gives back a passed result")]
    public void ThenPassed()
    {
        Assert.True(_result!.Passed, string.Join("\n", _result.Evidence));
        Assert.Empty(_result.Evidence);
    }

    [Then("the test gate gives back a failed result")]
    public void ThenFailed() => Assert.False(_result!.Passed);

    [Then("the commands ran in the order restore, build, format check, test, integration and no other")]
    public void ThenOrder() => Assert.Equal(["restore", "build", "format", "test", "run"], _harness.Seen.Select(c => c.Subcommand));

    [Then("the integration command ran with the names {string} and {string}")]
    public void ThenIntegrationNames(string first, string second) =>
        Assert.Equal([first, second], _harness.Seen.Single(c => c.Subcommand is "run").EnvNames);

    [Then("each of the other four commands ran with no name")]
    public void ThenOthersNoName()
    {
        List<SeenCommand> others = [.. _harness.Seen.Where(c => c.Subcommand is not "run")];
        Assert.Equal(4, others.Count);
        Assert.All(others, c => Assert.Empty(c.EnvNames));
    }

    [Then("no command ran")]
    public void ThenNoCommand()
    {
        Assert.Empty(_harness.Seen);
        Assert.Equal(0, _harness.DockerCommands);
    }

    [Then("the evidence names {string} and {string}")]
    public void ThenNamesTwo(string first, string second) => Joined(first, second);

    [Then("the evidence names {string}")]
    public void ThenNamesOne(string first) => Joined(first);

    [Then("the evidence names {string}, {string} and {string}")]
    public void ThenNamesThree(string first, string second, string third) => Joined(first, second, third);

    [Then("the evidence names {string} and {string} and holds {string}")]
    public void ThenNamesAndHolds(string first, string second, string message) => Joined(first, second, message);

    [Then("the restore was the last command")]
    public void ThenRestoreLast() => Last("restore");

    [Then("the build was the last command")]
    public void ThenBuildLast() => Last("build");

    [Then("the format check was the last command")]
    public void ThenFormatLast() => Last("format");

    [Then("the test command was the last command")]
    public void ThenTestLast() => Last("test");

    [Then("the evidence names the restore and holds its exit code and both reasons")]
    public void ThenRestoreEvidence() => Command("restore");

    [Then("the evidence names the build and holds its exit code and both reasons")]
    public void ThenBuildEvidence() => Command("build");

    [Then("the evidence names the format check and holds its exit code and both reasons")]
    public void ThenFormatEvidence() => Command("format");

    [Then("the evidence names the test command and holds the code 1 and both reasons")]
    public void ThenTestEvidence() => Command("test");

    [Then("the evidence is one text")]
    public void ThenOneText() => Assert.Single(_result!.Evidence);

    [Then("the evidence names the test command and holds the code 1 and the reason")]
    public void ThenTestReason()
    {
        string text = Assert.Single(_result!.Evidence);
        Assert.Contains("dotnet test", text);
        Assert.Contains("code 1", text);
        Assert.Contains(Out, text);
    }

    [Then("the evidence names the integration command and holds the code 1 and the summary")]
    public void ThenIntegrationEvidence()
    {
        string text = Assert.Single(_result!.Evidence);
        Assert.Contains("dotnet run", text);
        Assert.Contains("code 1", text);
        Assert.Contains("Failed: 1", text);
    }

    private void Fail(string subcommand) =>
        _harness.Answers[subcommand] = new CommandResult { ExitCode = subcommand is "format" ? 2 : 1, StandardOutput = Out, StandardError = Err };

    private async Task Run(IReadOnlyList<string> names)
    {
        if (!_started)
        {
            await _harness.StartAsync(TestContext.Current.CancellationToken);
            _started = true;
        }

        _result = await _harness.RunAsync(names, TestContext.Current.CancellationToken);
    }

    private void Joined(params string[] parts)
    {
        string all = string.Join("\n", _result!.Evidence);
        foreach (string part in parts)
        {
            Assert.Contains(part, all);
        }
    }

    private void Last(string subcommand) => Assert.Equal(subcommand, _harness.Seen[^1].Subcommand);

    private void Command(string subcommand)
    {
        string text = Assert.Single(_result!.Evidence);
        int code = subcommand is "format" ? 2 : 1;
        Assert.Contains($"dotnet {subcommand}", text);
        Assert.Contains($"code {code}", text);
        Assert.Contains(Out, text);
        Assert.Contains(Err, text);
    }
}
