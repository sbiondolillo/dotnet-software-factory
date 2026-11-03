using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class ScopeGateTests : IDisposable
{
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-scope-tree-");

    public void Dispose() => _tree.Delete(recursive: true);

    private Task<GateOutcome> RunAsync(IReadOnlyList<string> scope, params string[] changed)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(
                spec.Arguments is ["diff", "--name-only", "base"] ? new CommandResult { StandardOutput = string.Join('\n', changed) + "\n" } : new CommandResult()));
        var gate = new ScopeGate(new Git(mock.Object), "base", _tree.FullName, scope);
        Assert.Equal(GateName.Scope, GateSandbox.NameOf(gate));
        return GateSandbox.RunAsync(gate, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AChangeInTheScopeIsInScopeWithNoRedoText()
    {
        GateOutcome outcome = await RunAsync(["README.md", "docs/"], "README.md", "docs/a.md", ".factory/plan.md");
        Assert.Equal("in scope", outcome.Result);
        Assert.Empty(outcome.RedoTexts);
    }

    [Fact]
    public async Task AChangeOutsideTheScopeIsOutOfScopeWithItsPath()
    {
        GateOutcome outcome = await RunAsync(["README.md"], "README.md", "src/Core/Greeter.cs");
        Assert.Equal("out of scope", outcome.Result);
        Assert.Equal(["outside the scope: src/Core/Greeter.cs"], outcome.RedoTexts);
    }

    [Fact]
    public async Task AChangeToAFeatureFileIsOutOfScopeAsAPinFaultEvenInTheScope()
    {
        GateOutcome outcome = await RunAsync(["specs/features/"], "specs/features/a.feature");
        Assert.Equal("out of scope", outcome.Result);
        Assert.Equal(["pin: specs/features/a.feature"], outcome.RedoTexts);
    }
}
