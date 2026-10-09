using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class StarterWriteIssueSkillSteps(StarterState state)
{
    private string _body = "";
    private string _check = "";

    [Then("the copy's directory {string} holds the files {string}, {string} and {string} and no other file")]
    public void ThenTheCopysDirectoryHolds(string directory, string first, string second, string third)
        => StarterChecks.HoldsOnly(Path.Combine(state.Copy, directory), [first, second, third]);

    [Given("the draft body")]
    public void GivenTheDraftBody(string docString) => _body = docString;

    [Given("the draft body, with CRLF line ends")]
    public void GivenTheDraftBodyWithCrlf(string docString) => _body = docString.Replace("\n", "\r\n", StringComparison.Ordinal);

    // The repo's .editorconfig trims a trailing space in a feature file, so the scenario names each space and tab.
    [Given("the draft body, with each {string} as a space and each {string} as a tab")]
    public void GivenTheDraftBodyWithMarks(string space, string tab, string docString)
        => _body = docString.Replace(space, " ", StringComparison.Ordinal).Replace(tab, "\t", StringComparison.Ordinal);

    // The step runs the program that the copy's file holds, so an edit of that file is what the scenario tests.
    [When("the awk program of the copy's {string} saves the check of that body")]
    public async Task WhenTheAwkProgramSaves(string file)
    {
        string[] blocks = [.. FencedBlocks(File.ReadAllLines(Path.Combine(state.Copy, file)))
            .Where(block => block.StartsWith("awk '", StringComparison.Ordinal))];
        Assert.True(blocks.Length == 1, $"{file} holds {blocks.Length} fenced blocks that start with awk ', and the step needs one");
        string program = blocks[0];
        Assert.True(program.Contains("<draft>", StringComparison.Ordinal) && program.Contains("<CHECK>", StringComparison.Ordinal),
            "the awk block lacks <draft> or <CHECK>");

        string directory = Path.GetDirectoryName(state.Copy)!;
        string draft = Path.Combine(directory, "draft.md");
        _check = Path.Combine(directory, "check.sh");
        await File.WriteAllTextAsync(draft, _body);

        CommandResult result = await new CommandRunner().RunAsync(new CommandSpec
        {
            Command = "sh",
            Arguments = ["-c", program.Replace("<draft>", $"'{draft}'", StringComparison.Ordinal).Replace("<CHECK>", $"'{_check}'", StringComparison.Ordinal)],
            Timeout = TimeSpan.FromSeconds(30),
        });
        Assert.True(result.ExitCode == 0, $"the awk program exits with {result.ExitCode}: {result.StandardError}");
    }

    [Then("the saved check is this text")]
    public void ThenTheSavedCheckIs(string docString) => StarterChecks.TextEquals(_check, docString);

    [Then("the saved check is empty")]
    public void ThenTheSavedCheckIsEmpty() => Assert.Equal(0, new FileInfo(_check).Length);

    [Then("CleanupBody reads the same check from that body")]
    public void ThenCleanupBodyReadsTheSameCheck()
    {
        string? check = CleanupBody.ReadCheck(_body);
        Assert.Equal(check is null ? "" : check + "\n", File.ReadAllText(_check));
    }

    private static IEnumerable<string> FencedBlocks(string[] lines)
    {
        List<string>? block = null;
        foreach (string line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (block is not null)
                {
                    yield return string.Join('\n', block);
                }

                block = block is null ? [] : null;
                continue;
            }

            block?.Add(line);
        }
    }
}
