using Core;
using Moq;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class TestGateTests : IDisposable
{
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-testgate-unit-");
    private readonly List<string> _subcommands = [];
    private readonly List<CancellationToken> _tokens = [];
    private string? _trx;
    private int _testCode;
    private Sandbox? _sandbox;

    public void Dispose()
    {
        _sandbox?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _tree.Delete(recursive: true);
    }

    private static string Passing(string method) => TrxBuilder.Build([("Specs.Features.AFeature", method, "Passed", null)]);

    private void Feature(string name, params string[] titles)
    {
        string file = Path.Combine(_tree.FullName, "specs", "features", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "Feature: F\n\n" + string.Join("\n", titles.Select(title => $"  Scenario: {title}\n    Given x\n")));
    }

    private async Task<TestGateResult> RunAsync(Pin? pin = null, CancellationToken token = default)
    {
        pin ??= Pin.Take(_tree.FullName);
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken t) =>
            {
                string[] arguments = [.. spec.Arguments];
                if (arguments[0] is not "exec")
                {
                    return Task.FromResult(new CommandResult { StandardOutput = "abc\n" });
                }

                _tokens.Add(t);
                string[] command = arguments[Array.IndexOf(arguments, "dotnet")..];
                _subcommands.Add(command[1]);
                if (command[1] is "test")
                {
                    if (_trx is not null)
                    {
                        string directory = Path.Combine(_tree.FullName, command[Array.IndexOf(command, "--results-directory") + 1]["/work/".Length..]);
                        File.WriteAllText(Path.Combine(directory, "gate.trx"), _trx);
                    }

                    return Task.FromResult(new CommandResult { ExitCode = _testCode });
                }

                return Task.FromResult(new CommandResult());
            });
        _sandbox = await Sandbox.StartAsync(mock.Object, "t:1", _tree.FullName, _tree.FullName, token);
        return await new TestGate(_sandbox).RunAsync(_tree.FullName, pin, [], token);
    }

    [Fact]
    public async Task AnAddedPinnedFileAloneRunsNoCommandAndIsNamed()
    {
        Pin pin = Pin.Take(_tree.FullName);
        File.WriteAllText(Path.Combine(_tree.FullName, "global.json"), "{}");

        TestGateResult result = await RunAsync(pin, TestContext.Current.CancellationToken);

        Assert.False(result.Passed);
        Assert.Equal(["global.json"], result.Evidence);
        Assert.Empty(_subcommands);
    }

    [Fact]
    public async Task AFailedTestWithNoMessageStillGivesClassAndMethod()
    {
        _trx = TrxBuilder.Build([("A.B", "M", "Failed", null)]);
        _testCode = 1;

        TestGateResult result = await RunAsync(token: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Evidence.Count(text => text.Contains("A.B", StringComparison.Ordinal) && text.Contains('M', StringComparison.Ordinal) && text.StartsWith("failed test", StringComparison.Ordinal)));
        Assert.Equal(1, result.Evidence.Count(text => text.StartsWith("dotnet test", StringComparison.Ordinal)));
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public async Task AnUnreadableTrxFailsWithTheTestCommandAndTheFile()
    {
        _trx = "not xml";

        TestGateResult result = await RunAsync(token: TestContext.Current.CancellationToken);

        Assert.False(result.Passed);
        string text = Assert.Single(result.Evidence);
        Assert.Contains("dotnet test", text, StringComparison.Ordinal);
        Assert.Contains("gate.trx", text, StringComparison.Ordinal);
        Assert.Equal("test", _subcommands[^1]);
    }

    [Fact]
    public async Task EachMissingScenarioGivesOneTextWithItsOwnPath()
    {
        Feature("a.feature", "First one");
        Feature("b.feature", "Second one");
        _trx = Passing("Unrelated");

        TestGateResult result = await RunAsync(token: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Evidence.Count);
        Assert.Contains("specs/features/a.feature", result.Evidence[0], StringComparison.Ordinal);
        Assert.Contains("FirstOne", result.Evidence[0], StringComparison.Ordinal);
        Assert.Contains("specs/features/b.feature", result.Evidence[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoFeatureFileLeavesTheDecisionToCommandsAndTrx()
    {
        _trx = Passing("Anything");

        TestGateResult result = await RunAsync(token: TestContext.Current.CancellationToken);

        Assert.True(result.Passed, string.Join("\n", result.Evidence));
        Assert.Equal("run", _subcommands[^1]);
    }

    [Fact]
    public async Task TheTestNameOfAPunctuatedTitleComesFromFeatureFiles()
    {
        Feature("a.feature", "A per-location greeting");
        _trx = Passing(FeatureFiles.ComputeTestName("A per-location greeting"));

        TestGateResult result = await RunAsync(token: TestContext.Current.CancellationToken);

        Assert.True(result.Passed, string.Join("\n", result.Evidence));
    }

    [Fact]
    public async Task AFailedResultFailsEvenWhenAnotherClassPassedTheSameName()
    {
        Feature("a.feature", "Same name");
        _trx = TrxBuilder.Build([("A.One", "SameName", "Passed", null), ("A.Two", "SameName", "Failed", "boom")]);

        TestGateResult result = await RunAsync(token: TestContext.Current.CancellationToken);

        Assert.False(result.Passed);
        Assert.Contains(result.Evidence, text => text.Contains("A.Two", StringComparison.Ordinal) && text.Contains("boom", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("skipped")]
    [InlineData("failed")]
    [InlineData("missing")]
    public async Task ATrxFailureRunsNoIntegrationCommand(string kind)
    {
        Feature("a.feature", "A scenario");
        _trx = kind switch
        {
            "skipped" => TrxBuilder.Build([("A.B", "AScenario", "Passed", null), ("A.B", "S", "NotExecuted", null)]),
            "failed" => TrxBuilder.Build([("A.B", "AScenario", "Failed", "x")]),
            _ => Passing("Other"),
        };

        TestGateResult result = await RunAsync(token: TestContext.Current.CancellationToken);

        Assert.False(result.Passed);
        Assert.Equal("test", _subcommands[^1]);
    }

    [Fact]
    public async Task TheTokenReachesEveryCommand()
    {
        _trx = Passing("Anything");
        using var source = new CancellationTokenSource();

        TestGateResult result = await RunAsync(token: source.Token);

        Assert.True(result.Passed);
        Assert.Equal(5, _tokens.Count);
        Assert.All(_tokens, token => Assert.Equal(source.Token, token));
    }
}
