using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class TestGateSteps : IDisposable
{
    public TestGateSteps(ResultState state) => state.Outcome = () => (_result!.Passed, string.Join("\n", _result.Evidence));

    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ScenarioTest = "TheTotalIsTheSumOfThePrices";
    private const string FeatureClass = "Specs.Features.CheckoutFeature";
    private static readonly string[] Subcommands = ["restore", "build", "format", "test", "run"];

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-testgate-steps-");
    private readonly List<string[]> _recorded = [];
    private readonly Dictionary<string, CommandResult> _results = Subcommands.ToDictionary(name => name, _ => new CommandResult());
    private List<(string Class, string Method, string Outcome, string? Message)>? _trx =
        [(FeatureClass, ScenarioTest, "Passed", null)];

    private Sandbox? _sandbox;
    private Pin? _pin;
    private TestGate? _gate;
    private TestGateResult? _result;

    public void Dispose()
    {
        _sandbox?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _directory.Delete(recursive: true);
    }

    private string Joined => string.Join("\n", _result!.Evidence);

    [Given("a TestGate built over a Sandbox over a mock CommandRunner")]
    public async Task GivenATestGate()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        _sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _directory.FullName, _directory.FullName, TestContext.Current.CancellationToken);
        _gate = new TestGate(_sandbox);
        _recorded.Clear();
    }

    private CommandResult Answer(CommandSpec spec)
    {
        string[] arguments = [.. spec.Arguments];
        switch (arguments[0])
        {
            case "run":
                return new CommandResult { StandardOutput = ContainerId + "\n" };
            case "exec":
                int program = Array.IndexOf(arguments, "dotnet");
                string[] command = arguments[program..];
                _recorded.Add(arguments);
                if (command[1] is "test" && _trx is not null)
                {
                    WriteTrx(command);
                }

                return _results[command[1]];
            default:
                return new CommandResult();
        }
    }

    private void WriteTrx(string[] command)
    {
        string name = command.Single(argument => argument.StartsWith("trx;LogFileName=", StringComparison.Ordinal))["trx;LogFileName=".Length..];
        string container = command[Array.IndexOf(command, "--results-directory") + 1];
        string host = Path.Combine(_directory.FullName, container["/work/".Length..]);
        File.WriteAllText(Path.Combine(host, name), TrxBuilder.Build(_trx!));
    }

    [Given("the work tree holds {string} with the scenario {string}, pinned before the gate runs")]
    public void GivenTheWorkTreeHolds(string path, string title)
    {
        string file = Path.Combine(_directory.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"Feature: Checkout\n\n  Scenario: {title}\n    Given a cart\n");
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        _pin = Pin.Take(_directory.FullName);
    }

    [Given("a passing run")]
    public static void GivenAPassingRun()
    {
    }

    [Given("the pin finds no change")]
    public static void GivenNoChange()
    {
    }

    [Given("the earlier commands exit with the code {int}")]
    public void GivenEarlierCommands(int code)
    {
        foreach (string name in new[] { "restore", "build", "format" })
        {
            _results[name] = new CommandResult { ExitCode = code };
        }
    }

    [Given("the pin finds {string} changed and {string} removed")]
    public void GivenThePinFinds(string changed, string removed)
    {
        File.AppendAllText(Path.Combine(_directory.FullName, changed), "\n  Scenario: Another\n");
        File.Delete(Path.Combine(_directory.FullName, removed));
    }

    [Given("the restore exits with the code {int} and writes a reason to standard output and a reason to standard error")]
    public void GivenRestore(int code) => Fail("restore", code);

    [Given("the build exits with the code {int} and writes a reason to standard output and a reason to standard error")]
    public void GivenBuild(int code) => Fail("build", code);

    [Given("the format check exits with the code {int} and writes a reason to standard output and a reason to standard error")]
    public void GivenFormat(int code) => Fail("format", code);

    [Given("the test command exits with the code {int} and writes a reason to standard output and a reason to standard error")]
    public void GivenTest(int code) => Fail("test", code);

    [Given("the test command exits with the code {int} and writes a reason to standard output and writes no TRX file")]
    public void GivenTestNoTrx(int code)
    {
        _results["test"] = new CommandResult { ExitCode = code, StandardOutput = "test reason out" };
        _trx = null;
    }

    [Given("the test command writes a passing TRX file")]
    public void GivenTestPassingTrx() => _trx = [(FeatureClass, ScenarioTest, "Passed", null)];

    [Given("the test command exits with the code {int} and writes a TRX file that holds the failed test {string} of the class {string} with the message {string}")]
    public void GivenFailedTest(int code, string method, string className, string message)
    {
        _results["test"] = new CommandResult { ExitCode = code };
        _trx = [(className, method, "Failed", message)];
    }

    [Given("the test command exits with the code {int} and writes a passing TRX file that also holds the skipped test {string} of the class {string}")]
    public void GivenSkippedTest(int code, string method, string className)
    {
        _results["test"] = new CommandResult { ExitCode = code };
        _trx = [(FeatureClass, ScenarioTest, "Passed", null), (className, method, "NotExecuted", null)];
    }

    [Given("the test command exits with the code {int} and writes a TRX file that holds the passed test {string} of the class {string} and no test named {string}")]
    public void GivenMissing(int code, string method, string className, string absent)
    {
        Assert.NotEqual(absent, method);
        _results["test"] = new CommandResult { ExitCode = code };
        _trx = [(className, method, "Passed", null)];
    }

    [Given("the test command exits with the code {int} and writes a TRX file that holds two results of the test {string} of the class {string}, one passed and one failed")]
    public void GivenTwoResults(int code, string method, string className)
    {
        _results["test"] = new CommandResult { ExitCode = code };
        _trx = [(className, method, "Passed", null), (className, method, "Failed", "one row failed")];
    }

    [Given("each of the four commands writes a line to standard error")]
    public void GivenStderr()
    {
        foreach (string name in new[] { "restore", "build", "format", "test" })
        {
            _results[name] = new CommandResult { StandardError = "An issue was encountered verifying workloads" };
        }
    }

    [Given("the work tree holds the TRX file of an earlier run of the gate with a failed test")]
    public void GivenEarlierFailed() => WriteEarlier("Failed");

    [Given("the work tree holds the TRX file of an earlier run of the gate with a passed test")]
    public void GivenEarlierPassed() => WriteEarlier("Passed");

    [Given("the four commands exit with the code 0 and the test command writes a passing TRX file")]
    public void GivenFourPass() => _trx = [(FeatureClass, ScenarioTest, "Passed", null)];

    [Given("the integration command exits with the code {int} and writes a summary to standard output")]
    public void GivenIntegration(int code) =>
        _results["run"] = new CommandResult { ExitCode = code, StandardOutput = "Integration  Total: 3, Errors: 0, Failed: 1" };

    [When("a caller runs the test gate")]
    public async Task WhenRun() => await RunAsync([]);

    [When("a caller runs the test gate with the secret names {string} and {string}")]
    public async Task WhenRunWithNames(string first, string second) => await RunAsync([first, second]);

    [Then("the Sandbox ran these commands in order: the restore, the build, the format check, the test, the integration command")]
    public void ThenOrder() => Assert.Equal(Subcommands, _recorded.Select(Subcommand));

    [Then("the Sandbox ran no other command")]
    public void ThenNoOther() => Assert.Equal(5, _recorded.Count);

    [Then("the Sandbox ran no command")]
    public void ThenNoCommand() => Assert.Empty(_recorded);

    [Then("the integration command ran with both names")]
    public void ThenIntegrationNames()
    {
        string[] run = _recorded.Single(arguments => Subcommand(arguments) is "run");
        Assert.Equal(["--env", "API_KEY", "--env", "SERVICE_TOKEN"], run.Skip(1).Take(4));
    }

    [Then("the restore, the build, the format check and the test ran with no name")]
    public void ThenNoNames() =>
        Assert.All(_recorded.Where(arguments => Subcommand(arguments) is not "run"), arguments => Assert.DoesNotContain("--env", arguments));

    [Then("the restore was the last command the Sandbox ran")]
    public void ThenRestoreLast() => Assert.Equal("restore", Subcommand(_recorded[^1]));

    [Then("the build was the last command the Sandbox ran")]
    public void ThenBuildLast() => Assert.Equal("build", Subcommand(_recorded[^1]));

    [Then("the format check was the last command the Sandbox ran")]
    public void ThenFormatLast() => Assert.Equal("format", Subcommand(_recorded[^1]));

    [Then("the test command was the last command the Sandbox ran")]
    public void ThenTestLast() => Assert.Equal("test", Subcommand(_recorded[^1]));

    [Then("the evidence names {string} and {string}")]
    public void ThenNames(string first, string second) => ContainsAll(first, second);

    [Then("the evidence names {string} and {string} and holds {string}")]
    public void ThenNamesAndHolds(string first, string second, string third) => ContainsAll(first, second, third);

    [Then("the evidence names {string}, {string} and {string}")]
    public void ThenNamesThree(string first, string second, string third) => ContainsAll(first, second, third);

    [Then("the evidence names {string}")]
    public void ThenNamesOne(string first) => ContainsAll(first);

    [Then("the evidence names the restore and holds its exit code and both reasons")]
    public void ThenRestoreEvidence() => ContainsAll("dotnet restore", "code 2", "restore reason out", "restore reason err");

    [Then("the evidence names the build and holds its exit code and both reasons")]
    public void ThenBuildEvidence() => ContainsAll("dotnet build", "code 2", "build reason out", "build reason err");

    [Then("the evidence names the format check and holds its exit code and both reasons")]
    public void ThenFormatEvidence() => ContainsAll("dotnet format", "code 2", "format reason out", "format reason err");

    [Then("the evidence names the test command and holds the code {int} and both reasons")]
    public void ThenTestEvidence(int code) => ContainsAll("dotnet test", $"code {code}", "test reason out", "test reason err");

    [Then("the evidence is one text")]
    public void ThenOneText() => Assert.Single(_result!.Evidence);

    [Then("the evidence names the test command and holds the code {int} and the reason")]
    public void ThenTestReason(int code) => ContainsAll("dotnet test", $"code {code}", "test reason out");

    [Then("the evidence names the integration command and holds the code {int} and the summary")]
    public void ThenIntegrationEvidence(int code) => ContainsAll("dotnet run", $"code {code}", "Integration  Total: 3, Errors: 0, Failed: 1");

    private static string Subcommand(string[] exec) => exec[Array.IndexOf(exec, "dotnet") + 1];

    private void Fail(string name, int code) =>
        _results[name] = new CommandResult { ExitCode = code, StandardOutput = $"{name} reason out", StandardError = $"{name} reason err" };

    private void ContainsAll(params string[] texts)
    {
        Assert.NotNull(_result);
        foreach (string text in texts)
        {
            Assert.Contains(text, Joined, StringComparison.Ordinal);
        }
    }

    private void WriteEarlier(string outcome)
    {
        string directory = Path.Combine(_directory.FullName, "TestResults");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "earlier.trx"), TrxBuilder.Build([("Specs.Unit.EarlierTests", "Earlier", outcome, outcome is "Failed" ? "earlier failure" : null)]));
    }

    private async Task RunAsync(string[] names) =>
        _result = await _gate!.RunAsync(_directory.FullName, _pin!, names, TestContext.Current.CancellationToken);
}
