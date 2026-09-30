using System.Xml.Linq;
using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class TestGateTests : IDisposable
{
    private const string Id = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-testgate-unit-");
    private readonly List<string> _ran = [];
    private readonly Dictionary<string, int> _codes = [];
    private List<(string Method, string Outcome)>? _trx;
    private bool _writeTrx = true;

    public TestGateTests()
    {
        Feature("a.feature", "First one");
        Feature("b.feature", "Second one");
        Trx = [("FirstOne", "Passed"), ("SecondOne", "Passed")];
    }

    private List<(string Method, string Outcome)> Trx
    {
        get => _trx!;
        set => _trx = value;
    }

    public void Dispose() => _tree.Delete(recursive: true);

    [Fact]
    public async Task AnAddedFileEndsTheGateBeforeAnyCommand()
    {
        Pin pin = Pin.Take(_tree.FullName);
        Feature("c.feature", "Third one");
        TestGateResult result = await RunAsync(pin);
        Assert.False(result.Passed);
        Assert.Equal(["specs/features/c.feature"], result.Evidence);
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task AChangedFileAndARemovedFileEndTheGateBeforeAnyCommand()
    {
        File.WriteAllText(Path.Combine(_tree.FullName, "global.json"), "{}");
        Pin pin = Pin.Take(_tree.FullName);
        File.AppendAllText(Path.Combine(_tree.FullName, "specs", "features", "a.feature"), "\n# changed\n");
        File.Delete(Path.Combine(_tree.FullName, "global.json"));
        TestGateResult result = await RunAsync(pin);
        Assert.False(result.Passed);
        Assert.Equal(["specs/features/a.feature", "global.json"], result.Evidence);
        Assert.Empty(_ran);
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("format")]
    [InlineData("run")]
    public async Task AFailingCommandIsTheLastAndItsEvidenceHoldsTheCodeAndBothStreams(string sub)
    {
        _codes[sub] = 3;
        TestGateResult result = await RunAsync(Pin.Take(_tree.FullName));
        Assert.False(result.Passed);
        Assert.Equal(sub, _ran[^1]);
        string text = Assert.Single(result.Evidence);
        Assert.Contains($"dotnet {sub}", text);
        Assert.Contains("code 3", text);
        Assert.Contains("out " + sub, text);
        Assert.Contains("err " + sub, text);
    }

    [Fact]
    public async Task AFailedTestPutsItsClassMethodAndMessageInTheEvidence()
    {
        _codes["test"] = 1;
        Trx = [("FirstOne", "Failed"), ("SecondOne", "Passed")];
        TestGateResult result = await RunAsync(Pin.Take(_tree.FullName));
        Assert.False(result.Passed);
        Assert.Equal("test", _ran[^1]);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("Some.Class", text);
        Assert.Contains("FirstOne", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public async Task ASkippedTestFailsTheGateWithItsClassAndMethod()
    {
        Trx = [("FirstOne", "Passed"), ("SecondOne", "Passed"), ("Later", "NotExecuted")];
        TestGateResult result = await RunAsync(Pin.Take(_tree.FullName));
        Assert.False(result.Passed);
        Assert.Equal("test", _ran[^1]);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("Some.Class", text);
        Assert.Contains("Later", text);
    }

    [Fact]
    public async Task OneMissingScenarioInEachFeatureGivesOneTextForEach()
    {
        Trx = [];
        TestGateResult result = await RunAsync(Pin.Take(_tree.FullName));
        Assert.False(result.Passed);
        Assert.Equal(2, result.Evidence.Count);
        Assert.Contains("specs/features/a.feature", result.Evidence[0]);
        Assert.Contains("First one", result.Evidence[0]);
        Assert.Contains("FirstOne", result.Evidence[0]);
        Assert.Contains("specs/features/b.feature", result.Evidence[1]);
    }

    [Fact]
    public async Task AFailedResultBesideAPassedOneFailsTheGate()
    {
        Trx = [("FirstOne", "Passed"), ("FirstOne", "Failed"), ("SecondOne", "Passed")];
        TestGateResult result = await RunAsync(Pin.Take(_tree.FullName));
        Assert.False(result.Passed);
        Assert.Contains("FirstOne", Assert.Single(result.Evidence));
    }

    [Fact]
    public async Task AnEarlierTrxFileIsNotReadWhenTheTestCommandWritesNone()
    {
        string earlier = Path.Combine(_tree.FullName, "TestResults");
        Directory.CreateDirectory(earlier);
        Write(Path.Combine(earlier, "old.trx"), [("FirstOne", "Passed"), ("SecondOne", "Passed")]);
        _codes["test"] = 1;
        _writeTrx = false;
        TestGateResult result = await RunAsync(Pin.Take(_tree.FullName));
        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("dotnet test", text);
        Assert.Contains("code 1", text);
    }

    [Fact]
    public async Task ANonZeroTestCodeWithOnlyPassingTestsFailsWithTheCommandEvidence()
    {
        _codes["test"] = 1;
        TestGateResult result = await RunAsync(Pin.Take(_tree.FullName));
        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("code 1", text);
        Assert.Equal("test", _ran[^1]);
    }

    [Fact]
    public async Task APassingRunPasses()
    {
        TestGateResult result = await RunAsync(Pin.Take(_tree.FullName));
        Assert.True(result.Passed);
        Assert.Empty(result.Evidence);
        Assert.Equal(["restore", "build", "format", "test", "run"], _ran);
        Assert.False(Directory.Exists(Path.Combine(_tree.FullName, "TestResults")) && Directory.EnumerateFileSystemEntries(Path.Combine(_tree.FullName, "TestResults")).Any());
    }

    private static void Write(string path, List<(string Method, string Outcome)> tests)
    {
        var definitions = new XElement(Ns + "TestDefinitions");
        var results = new XElement(Ns + "Results");
        foreach (var (method, outcome) in tests)
        {
            string id = Guid.NewGuid().ToString();
            definitions.Add(new XElement(
                Ns + "UnitTest",
                new XAttribute("id", id),
                new XElement(Ns + "TestMethod", new XAttribute("className", "Some.Class"), new XAttribute("name", method))));
            var result = new XElement(Ns + "UnitTestResult", new XAttribute("testId", id), new XAttribute("outcome", outcome));
            if (outcome is "Failed")
            {
                result.Add(new XElement(Ns + "Output", new XElement(Ns + "ErrorInfo", new XElement(Ns + "Message", "boom"))));
            }

            results.Add(result);
        }

        new XDocument(new XElement(Ns + "TestRun", definitions, results)).Save(path);
    }

    private static string Sub(CommandSpec spec) => spec.Arguments[spec.Arguments.ToList().IndexOf(Id) + 2];

    private void Feature(string name, string title)
    {
        string directory = Path.Combine(_tree.FullName, "specs", "features");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), $"Feature: F\n\n  Scenario: {title}\n    Given x\n");
    }

    private async Task<TestGateResult> RunAsync(Pin pin)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                if (spec.Arguments[0] is "run")
                {
                    return Task.FromResult(new CommandResult { StandardOutput = Id });
                }

                if (spec.Arguments[0] is not "exec")
                {
                    return Task.FromResult(new CommandResult());
                }

                string sub = Sub(spec);
                _ran.Add(sub);
                if (sub is "test" && _writeTrx)
                {
                    List<string> arguments = [.. spec.Arguments];
                    string directory = arguments[arguments.IndexOf("--results-directory") + 1];
                    string host = Path.Combine(_tree.FullName, directory["/work/".Length..]);
                    Directory.CreateDirectory(host);
                    Write(Path.Combine(host, arguments[arguments.IndexOf("--logger") + 1]["trx;LogFileName=".Length..]), Trx);
                }

                return Task.FromResult(new CommandResult
                {
                    ExitCode = _codes.GetValueOrDefault(sub),
                    StandardOutput = "out " + sub,
                    StandardError = "err " + sub,
                });
            });
        await using Sandbox sandbox = await Sandbox.StartAsync(mock.Object, "t:t", _tree.FullName, _tree.FullName, TestContext.Current.CancellationToken);
        return await new TestGate(sandbox).RunAsync(_tree.FullName, pin, [], TestContext.Current.CancellationToken);
    }
}
