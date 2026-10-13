using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class SkipIntegrationOptionSteps(ConsoleProgramSteps console)
{
    /// <summary>Each command that <c>docker exec</c> ran: its arguments after the container id, joined with a space.</summary>
    private List<string> ExecCommands()
    {
        List<string> commands = [];
        foreach (CommandSpec spec in console.Harness.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
        {
            List<string> arguments = [.. spec.Arguments];
            int id = arguments.IndexOf(ConsoleHarness.ContainerId);
            commands.Add(string.Join(' ', arguments[(id + 1)..]));
        }

        return commands;
    }

    [Then("docker runs a command that starts with {string}")]
    public void ThenStartsWith(string prefix) => Assert.Contains(ExecCommands(), c => c.StartsWith(prefix, StringComparison.Ordinal));

    [Then("docker runs no command that holds {string}")]
    public void ThenNoneHolds(string text) => Assert.DoesNotContain(ExecCommands(), c => c.Contains(text, StringComparison.Ordinal));

    [Then("docker runs the command {string} one time or more")]
    public void ThenRuns(string command) => Assert.Contains(command, ExecCommands());
}
