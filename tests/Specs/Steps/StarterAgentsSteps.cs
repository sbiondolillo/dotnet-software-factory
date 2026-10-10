using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class StarterAgentsSteps(StarterState state)
{
    // The compare trims trailing line feeds on both sides.
    [Then("the copy's AGENTS.md at its root holds this text")]
    public void ThenTheCopysAgentsHolds(string docString)
        => StarterChecks.TextEquals(Path.Combine(state.Copy, "AGENTS.md"), docString);

    [Then("the comment in the copy's {string} starts with the line {string}")]
    public void ThenTheCommentStarts(string file, string line)
        => StarterChecks.CommentStarts(Path.Combine(state.Copy, file), line);

    [Then("the comment in the copy's {string} does not hold the line {string}")]
    public void ThenTheCommentLacks(string file, string line)
        => StarterChecks.CommentLacks(Path.Combine(state.Copy, file), line);

    [Then("the copy's file {string} holds this text")]
    public void ThenTheCopysFileHolds(string file, string docString)
        => StarterChecks.TextEquals(Path.Combine(state.Copy, file), docString);

    [Then("the copy's file {string} holds this text, with a line that holds {string}")]
    public void ThenTheCopysFileHoldsWithLine(string file, string line, string docString)
    {
        string path = Path.Combine(state.Copy, file);
        StarterChecks.TextEquals(path, docString);
        Assert.Contains(File.ReadAllLines(path), have => have.Contains(line, StringComparison.Ordinal));
    }

    [Then("the copy's file {string} holds the line {string}")]
    public void ThenTheCopysFileHoldsTheLine(string file, string line)
        => Assert.Contains(line, File.ReadAllLines(Path.Combine(state.Copy, file)));

    [Then("the copy's file {string} holds a line that holds {string}")]
    public void ThenTheCopysFileHoldsALineThatHolds(string file, string text)
        => Assert.Contains(File.ReadAllLines(Path.Combine(state.Copy, file)), line => line.Contains(text, StringComparison.Ordinal));

    [Then("the copy's file {string} holds no line that holds {string}")]
    public void ThenTheCopysFileHoldsNoLineThatHolds(string file, string text)
        => Assert.DoesNotContain(File.ReadAllLines(Path.Combine(state.Copy, file)), line => line.Contains(text, StringComparison.Ordinal));
}
