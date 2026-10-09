using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class CiWorkflowTests
{
    [Fact]
    public async Task TheSandboxImageStepRunsTheBuildOfSandboxImage()
    {
        string root = StarterChecks.RepoRoot();
        var recorded = new List<CommandSpec>();
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                recorded.Add(spec);
                return Task.FromResult(new CommandResult { ExitCode = spec.Arguments[0] is "image" ? 1 : 0 });
            });

        await new SandboxImage(mock.Object).EnsureAsync("factory-sandbox:integration", root, TestContext.Current.CancellationToken);

        IEnumerable<string> arguments = recorded[1].Arguments.Select(argument =>
            argument == root ? "."
            : argument.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? Path.GetRelativePath(root, argument)
            : argument.StartsWith("DOTNET_SDK_VERSION=", StringComparison.Ordinal) ? "DOTNET_SDK_VERSION=\"$(jq -r .sdk.version global.json)\""
            : argument);
        var jobs = (Dictionary<string, object?>)WorkflowChecks.Parse(File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml")))["jobs"]!;
        var steps = (List<object?>)((Dictionary<string, object?>)jobs["integration"]!)["steps"]!;
        Dictionary<string, object?> step = steps.OfType<Dictionary<string, object?>>().Single(step => step.GetValueOrDefault("name") is "Sandbox image");
        Assert.Equal(string.Join(' ', ["docker", .. arguments]), step["run"]);
    }
}
