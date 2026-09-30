using System.Xml.Linq;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class TestGateSteps : IDisposable
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Feature = "Feature: Checkout\n\n  Scenario: The total is the sum of the prices\n    Given a cart\n";
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private static readonly Dictionary<string, string> Subcommands = new()
    {
        ["restore"] = "restore",
        ["build"] = "build",
        ["format check"] = "format",
        ["test"] = "test",
        ["integration"] = "run",
    };

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-testgate-steps-");
    private readonly List<CommandSpec> _recorded = [];
    private readonly Dictionary<string, CommandResult> _results = [];
    private readonly string _featurePath;
    private Sandbox? _sandbox;
    private Pin? _pin;
    private List<(string Class, string Method, string Outcome, string? Message)>? _trxTests;
    private bool _writeTrx = true;
    private string _reason = "";
    private TestGateResult? _result;

    public TestGateSteps() =>
        _featurePath = Path.Combine(_directory.FullName, "specs", "features", "checkout.feature");

    private string Evidence => string.Join("\n---\n", _result!.Evidence);

    public void Dispose()
    {
        _sandbox?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _directory.Delete(recursive: true);
    }

    [Given("a TestGate built over a Sandbox over a mock CommandRunner")]
    public async Task GivenATestGate()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                if (spec.Arguments[0] is "run")
                {
                    return Task.FromResult(new CommandResult { StandardOutput = ContainerId + "\n" });
                }

                if (spec.Arguments[0] is not "exec")
                {
                    return Task.FromResult(new CommandResult());
                }

                _recorded.Add(spec);
                string subcommand = Sub(spec);
                if (subcommand is "test" && _writeTrx)
                {
                    WriteTrx(spec);
                }

                return Task.FromResult(_results.TryGetValue(subcommand, out CommandResult? result) ? result : new CommandResult());
            });
        _sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _directory.FullName, _directory.FullName, TestContext.Current.CancellationToken);
    }

    [Given("the work tree holds {string} pinned before the gate runs")]
    public void GivenPinned(string path)
    {
        Assert.Equal("specs/features/checkout.feature", path);
        Directory.CreateDirectory(Path.GetDirectoryName(_featurePath)!);
        File.WriteAllText(_featurePath, Feature);
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        _pin = Pin.Take(_directory.FullName);
    }

    [Given("the run is a passing run")]
    public void GivenPassing()
    {
        _results.Clear();
        _trxTests = null;
        _writeTrx = true;
    }

    [Given("the pin finds {string} changed and {string} removed")]
    public void GivenPinFinds(string changed, string removed)
    {
        File.AppendAllText(Path.Combine(_directory.FullName, changed), "\n# changed\n");
        File.Delete(Path.Combine(_directory.FullName, removed));
    }

    [Given(@"the ([a-z ]+) command exits with a code other than 0 and writes a reason to standard output and a reason to standard error")]
    public void GivenCommandFails(string command) =>
        _results[Subcommands[command]] = new CommandResult
        {
            ExitCode = 2,
            StandardOutput = $"out reason of {command}",
            StandardError = $"err reason of {command}",
        };

    [Given("each of the four commands writes a line to standard error")]
    public void GivenStandardError()
    {
        foreach (string sub in new[] { "restore", "build", "format", "test" })
        {
            _results[sub] = new CommandResult { StandardError = "a line on standard error" };
        }
    }

    [Given("the test command exits with the code {int} and writes a TRX file that holds the failed test {string} of the class {string} with the message {string}")]
    public void GivenFailedTest(int code, string method, string className, string message)
    {
        _results["test"] = new CommandResult { ExitCode = code };
        _trxTests = [(className, method, "Failed", message)];
    }

    [Given("the test command exits with the code {int} and writes a passing TRX file that also holds the skipped test {string} of the class {string}")]
    public void GivenSkippedTest(int code, string method, string className)
    {
        _results["test"] = new CommandResult { ExitCode = code };
        _trxTests = [("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Passed", null), (className, method, "NotExecuted", null)];
    }

    [Given("the test command exits with the code {int} and writes a TRX file that holds the passed test {string} of the class {string} and no test named {string}")]
    public void GivenOtherTest(int code, string method, string className, string absent)
    {
        Assert.NotEqual(absent, method);
        _results["test"] = new CommandResult { ExitCode = code };
        _trxTests = [(className, method, "Passed", null)];
    }

    [Given("the test command exits with the code {int} and writes a TRX file that holds two results of the test {string} of the class {string}, one passed and one failed")]
    public void GivenTwoResults(int code, string method, string className)
    {
        _results["test"] = new CommandResult { ExitCode = code };
        _trxTests = [(className, method, "Passed", null), (className, method, "Failed", "second result failed")];
    }

    [Given("the work tree holds the TRX file of an earlier run of the gate with a failed test")]
    public void GivenEarlierFailed() => WriteEarlier("Failed");

    [Given("the work tree holds the TRX file of an earlier run of the gate with a passed test")]
    public void GivenEarlierPassed() => WriteEarlier("Passed");

    [Given("the test command exits with the code {int}, writes a reason to standard output and writes no TRX file")]
    public void GivenNoTrx(int code)
    {
        _reason = "the reason the tests did not run";
        _results["test"] = new CommandResult { ExitCode = code, StandardOutput = _reason };
        _writeTrx = false;
    }

    [Given("the integration command exits with the code {int} and writes a summary to standard output")]
    public void GivenIntegrationSummary(int code)
    {
        _reason = "Integration  Total: 3, Errors: 0, Failed: 1, Skipped: 0";
        _results["run"] = new CommandResult { ExitCode = code, StandardOutput = _reason };
    }

    [When("a caller runs the test gate with no secret names")]
    public Task WhenNoNames() => Run([]);

    [When("a caller runs the test gate with the names {string} and {string}")]
    public Task WhenNames(string first, string second) => Run([first, second]);

    [Then("the test gate result is passed")]
    public void ThenPassed() => Assert.True(_result!.Passed, Evidence);

    [Then("the test gate result is failed")]
    public void ThenFailed() => Assert.False(_result!.Passed);

    [Then("the Sandbox ran the restore, the build, the format check, the test and the integration command in that order")]
    public void ThenOrder() => Assert.Equal(["restore", "build", "format", "test", "run"], _recorded.Select(Sub));

    [Then("the Sandbox ran no other command")]
    public void ThenNoOther() => Assert.Equal(5, _recorded.Count);

    [Then("the Sandbox ran no command")]
    public void ThenNoCommand() => Assert.Empty(_recorded);

    [Then("the integration command ran with the names {string} and {string}")]
    public void ThenIntegrationNames(string first, string second)
    {
        CommandSpec spec = _recorded.Single(s => Sub(s) is "run");
        Assert.Equal(["exec", "--env", first, "--env", second, ContainerId], spec.Arguments.Take(6));
    }

    [Then("each of the four other commands ran with no name")]
    public void ThenOthersNoName()
    {
        List<CommandSpec> others = [.. _recorded.Where(s => Sub(s) is not "run")];
        Assert.Equal(4, others.Count);
        Assert.All(others, s => Assert.Equal(["exec", ContainerId], s.Arguments.Take(2)));
    }

    [Then(@"the ([a-z ]+) command was the last command the Sandbox ran")]
    public void ThenLast(string command) => Assert.Equal(Subcommands[command], Sub(_recorded[^1]));

    [Then("the evidence names {string} and {string}")]
    public void ThenNames(string first, string second) => Assert.All(new[] { first, second }, item => Assert.Contains(item, Evidence));

    [Then("the evidence names {string}")]
    public void ThenNamesOne(string item) => Assert.Contains(item, Evidence);

    [Then("the evidence names {string} and {string} and holds {string}")]
    public void ThenNamesAndHolds(string first, string second, string message) =>
        Assert.All(new[] { first, second, message }, item => Assert.Contains(item, Evidence));

    [Then("the evidence names {string}, {string} and {string}")]
    public void ThenNamesThree(string first, string second, string third) =>
        Assert.All(new[] { first, second, third }, item => Assert.Contains(item, Evidence));

    [Then(@"the evidence names the ([a-z ]+) command and holds its exit code and both reasons")]
    public void ThenEvidenceCommand(string command)
    {
        string text = Assert.Single(_result!.Evidence);
        Assert.Contains($"dotnet {Subcommands[command]}", text);
        Assert.Contains("code 2", text);
        Assert.Contains($"out reason of {command}", text);
        Assert.Contains($"err reason of {command}", text);
    }

    [Then("the evidence is one text, which names the test command and holds the code {int} and the reason")]
    public void ThenOneText(int code)
    {
        string text = Assert.Single(_result!.Evidence);
        Assert.Contains("dotnet test", text);
        Assert.Contains($"code {code}", text);
        Assert.Contains(_reason, text);
    }

    [Then("the evidence names the integration command and holds the code {int} and the summary")]
    public void ThenIntegrationEvidence(int code)
    {
        string text = Assert.Single(_result!.Evidence);
        Assert.Contains("dotnet run", text);
        Assert.Contains($"code {code}", text);
        Assert.Contains(_reason, text);
    }

    private static string Sub(CommandSpec spec)
    {
        int id = spec.Arguments.ToList().IndexOf(ContainerId);
        return spec.Arguments[id + 2];
    }

    private async Task Run(string[] names) =>
        _result = await new TestGate(_sandbox!).RunAsync(_directory.FullName, _pin!, names, TestContext.Current.CancellationToken);

    private void WriteEarlier(string outcome)
    {
        string directory = Path.Combine(_directory.FullName, "TestResults", "earlier");
        Directory.CreateDirectory(directory);
        WriteTrxFile(
            Path.Combine(directory, "earlier.trx"),
            [("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", outcome, outcome is "Failed" ? "earlier failure" : null)]);
    }

    private void WriteTrx(CommandSpec spec)
    {
        List<string> arguments = [.. spec.Arguments];
        string directory = arguments[arguments.IndexOf("--results-directory") + 1];
        string logger = arguments[arguments.IndexOf("--logger") + 1];
        string file = logger["trx;LogFileName=".Length..];
        string host = Path.Combine(_directory.FullName, directory["/work/".Length..]);
        Directory.CreateDirectory(host);
        WriteTrxFile(
            Path.Combine(host, file),
            _trxTests ?? [("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Passed", null)]);
    }

    private static void WriteTrxFile(string path, List<(string Class, string Method, string Outcome, string? Message)> tests)
    {
        var definitions = new XElement(Ns + "TestDefinitions");
        var results = new XElement(Ns + "Results");
        foreach (var (className, method, outcome, message) in tests)
        {
            string id = Guid.NewGuid().ToString();
            definitions.Add(new XElement(
                Ns + "UnitTest",
                new XAttribute("id", id),
                new XElement(Ns + "TestMethod", new XAttribute("className", className), new XAttribute("name", method))));
            var result = new XElement(Ns + "UnitTestResult", new XAttribute("testId", id), new XAttribute("outcome", outcome));
            if (message is not null)
            {
                result.Add(new XElement(Ns + "Output", new XElement(Ns + "ErrorInfo", new XElement(Ns + "Message", message))));
            }

            results.Add(result);
        }

        new XDocument(new XElement(Ns + "TestRun", definitions, results)).Save(path);
    }
}
