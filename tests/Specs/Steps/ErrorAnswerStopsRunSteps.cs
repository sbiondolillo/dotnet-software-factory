using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

/// <summary>Binds the features in which a run stops at the first answer of claude with the error flag set: the no-label, cleanup and refactor runs share these steps.</summary>
[Binding]
public sealed class ErrorAnswerStopsRunSteps(FactoryRunSteps factory)
{
    private FactoryRunHarness H => factory.Harness;

    [Given("claude answers the plan prompt with the error flag set")]
    public void GivenPlanError() => H.PlanIsError = true;

    [Given("claude answers the first build prompt with the error flag set")]
    public void GivenBuildError() => H.ErrorBuilds.Add(1);

    [Given("claude answers the first build prompt with the error flag set and the result text {string}")]
    public void GivenBuildErrorText(string text)
    {
        H.ErrorBuilds.Add(1);
        H.ErrorText = text;
    }

    [Given("claude answers the first build prompt with the error flag set and no result text")]
    public void GivenBuildErrorNoText()
    {
        H.ErrorBuilds.Add(1);
        H.ErrorText = "";
    }

    [Given("claude answers the first review prompt with the error flag set")]
    public void GivenReviewError() => H.ErrorReviews.Add(1);

    [Then("the {word} prompt is the last docker exec command of the run")]
    public void ThenLast(string kind)
    {
        CommandSpec last = H.Commands.Last(c => c.Command is "docker" && c.Arguments[0] is "exec");
        Assert.Contains("claude", last.Arguments);
        string text = last.Arguments[last.Arguments.ToList().IndexOf("-p") + 1];
        string expected = kind switch
        {
            "plan" => "Plan the work",
            "review" => "Review the build",
            _ => "Do the ",
        };
        if (kind is "build")
        {
            Assert.True(text.StartsWith("Build the work", StringComparison.Ordinal) || text.StartsWith("Do the ", StringComparison.Ordinal), text);
        }
        else
        {
            Assert.StartsWith(expected, text, StringComparison.Ordinal);
        }
    }

    [Then("standard output holds the body of the comment that the run posts on the issue")]
    public void ThenStandardOutput()
    {
        string body = Assert.Single(H.CommentBodies);
        Assert.Contains(body + Environment.NewLine, H.Progress.ToString());
    }
}
