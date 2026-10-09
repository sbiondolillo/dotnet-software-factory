using System.Text.RegularExpressions;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

/// <summary>Applies a body that the starter teaches to the copy, as the factory applies the body of an issue.</summary>
[Binding]
public sealed partial class StarterIssueBodySteps(StarterState state)
{
    private string _body = "";
    private IssueScenariosResult? _result;

    private IssueScenariosResult Result => _result ?? throw new InvalidOperationException("no body was applied");

    [Given("the copy's {string} with {string} replaced by {string} and each other placeholder replaced by {string}")]
    public void GivenTheFilledTemplate(string file, string placeholder, string value, string other)
    {
        string text = File.ReadAllText(Path.Combine(state.Copy, file)).Replace(placeholder, value, StringComparison.Ordinal);
        _body = Placeholder().Replace(text, other);
    }

    [Given("the body in the markdown block of the copy's {string}")]
    public void GivenTheBodyInTheMarkdownBlock(string file)
    {
        Match block = MarkdownBlock().Match(File.ReadAllText(Path.Combine(state.Copy, file)));
        Assert.True(block.Success, $"{file} holds no markdown block");
        _body = block.Groups["body"].Value;
    }

    [When("the scenarios of that body are applied to the copy")]
    public void WhenTheBodyIsApplied() => _result = IssueScenarios.Apply(_body, state.Copy);

    [When("the scenarios of the copy's {string} are applied to the copy")]
    public void WhenTheFileIsApplied(string file)
    {
        _body = File.ReadAllText(Path.Combine(state.Copy, file));
        _result = IssueScenarios.Apply(_body, state.Copy);
    }

    [Then("the apply gives no fault")]
    public void ThenTheApplyGivesNoFault() => Assert.Empty(Result.Faults);

    [Then("the apply gives the one fault {string}")]
    public void ThenTheApplyGivesTheOneFault(string fault) => Assert.Equal([fault], Result.Faults);

    [Then("the copy holds {string}")]
    public void ThenTheCopyHolds(string file) => Assert.True(File.Exists(Path.Combine(state.Copy, file)), $"the copy lacks {file}");

    [Then("the copy's {string} ends with one blank line and the scenario of the template's block")]
    public void ThenTheFileEndsWithTheBlock(string file)
    {
        Match block = GherkinBlock().Match(_body);
        Assert.True(block.Success, "the body holds no block that starts with a path line");
        string scenario = block.Groups["scenario"].Value;
        string text = File.ReadAllText(Path.Combine(state.Copy, file));
        Assert.EndsWith("\n\n" + scenario, text, StringComparison.Ordinal);
        Assert.False(text.EndsWith("\n\n\n" + scenario, StringComparison.Ordinal), $"{file} holds more than one blank line before the scenario");
    }

    [Then("the copy's {string} ends with the scenario {string}")]
    public void ThenTheFileEndsWithTheScenario(string file, string name)
    {
        string? last = File.ReadAllLines(Path.Combine(state.Copy, file))
            .Select(line => line.Trim())
            .LastOrDefault(line => line.StartsWith("Scenario:", StringComparison.Ordinal));
        Assert.Equal($"Scenario: {name}", last);
    }

    [GeneratedRegex(@"<[^<>\n]+>")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^````markdown\n(?<body>.*?\n)````$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex MarkdownBlock();

    [GeneratedRegex(@"^```gherkin\n# specs/features/[^\n]+\n(?<scenario>.*?\n)```$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex GherkinBlock();
}
