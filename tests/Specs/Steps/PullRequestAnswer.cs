namespace Specs.Steps;

/// <summary>GitHub's canned answer to the open of a pull request, shared by the step classes that bind its step.</summary>
public sealed class PullRequestAnswer
{
    public int Number { get; set; } = 10;

    public string Url { get; set; } = "https://github.com/sbiondolillo/factory-scratch/pull/10";

    public int Status { get; set; } = 201;
}
