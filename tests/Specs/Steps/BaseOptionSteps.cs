using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class BaseOptionSteps(ConsoleProgramSteps console)
{
    private List<CommandSpec> Git => [.. console.Harness.Commands.Where(c => c.Command is "git")];

    [Then("git checks out the commit {string} detached in the clone, after the clone and before the first head read")]
    public void ThenChecksOut(string commit)
    {
        List<CommandSpec> git = Git;
        int clone = git.FindIndex(c => c.Arguments[0] is "clone");
        int checkout = git.FindIndex(c => c.Arguments is ["checkout", "--detach", _] && c.Arguments[2] == commit);
        int read = git.FindIndex(c => c.Arguments[0] is "rev-parse");
        Assert.True(clone >= 0 && checkout > clone && read > checkout, $"git commands: {string.Join(" | ", git.Select(c => string.Join(' ', c.Arguments)))}");
        Assert.Equal(git[clone].Arguments[2], git[checkout].WorkingDirectory);
    }

    [Then("git checks out no commit detached")]
    public void ThenNoDetach() => Assert.DoesNotContain(Git, c => c.Arguments is ["checkout", "--detach", ..]);
}
