using Core;
using Xunit;

namespace Specs.Unit;

public sealed class SkipIntegrationRunTests
{
    private const string GateTest = "dotnet test --no-restore --logger trx --results-directory /work/.test-gate-";
    private const string Integration = "dotnet run --no-restore --project tests/Integration";

    [Theory]
    [InlineData("refactor", "## Check\n\n```sh\ntest -f src/Core/Greeting.cs\n```\n", true)]
    [InlineData("refactor", "## Check\n\n```sh\ntest -f src/Core/Greeting.cs\n```\n", false)]
    [InlineData("cleanup", "## Scope\n.editorconfig\n\n## Check\n\n```sh\ntrue\n```\n", true)]
    [InlineData("cleanup", "## Scope\n.editorconfig\n\n## Check\n\n```sh\ntrue\n```\n", false)]
    [InlineData("reframe", "## Scope\n.editorconfig\n\n## Check\n\n```sh\ntrue\n```\n", true)]
    [InlineData("reframe", "## Scope\n.editorconfig\n\n## Check\n\n```sh\ntrue\n```\n", false)]
    public async Task TheTestGateOfALabelledRunRunsTheIntegrationCommandUnlessTheRunSkipsIt(string label, string body, bool skip)
    {
        using var h = new FactoryRunHarness { Title = "Tidy it", Body = body, SkipIntegration = skip };
        h.Labels.Add(label);
        if (label is not "refactor")
        {
            h.FirstBuildChanges.Add(".editorconfig");
            h.FirstBuildWrites.Add(".editorconfig");
        }

        await h.RunAsync(TestContext.Current.CancellationToken);

        List<string> commands = [.. h.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec").Select(c => string.Join(' ', c.Arguments))];
        Assert.Contains(commands, c => c.Contains(GateTest, StringComparison.Ordinal));
        Assert.Equal(!skip, commands.Any(c => c.Contains(Integration, StringComparison.Ordinal)));
    }
}
