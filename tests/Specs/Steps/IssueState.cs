namespace Specs.Steps;

/// <summary>The issue of a scenario, shared by the step classes that bind the issue step.</summary>
public sealed class IssueState
{
    public string Title { get; set; } = "";

    public string Body { get; set; } = "";
}
