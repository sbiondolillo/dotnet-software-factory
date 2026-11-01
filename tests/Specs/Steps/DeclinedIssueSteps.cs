using Reqnroll;
using Reqnroll.BoDi;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class DeclinedIssueSteps(IObjectContainer container)
{
    [Given("the issue {int} has the label {string} and this body:")]
    public void GivenLabelAndBody(int number, string label, string body)
    {
        Assert.Equal(12, number);
        Specs.Unit.ConsoleHarness console = container.Resolve<ConsoleProgramSteps>().Harness;
        console.Labels.Add(label);
        console.IssueBody = body;
    }

    [Given("the issue {int} has no label and this body:")]
    public void GivenNoLabelAndBody(int number, string body)
    {
        Assert.Equal(12, number);
        container.Resolve<ConsoleProgramSteps>().Harness.IssueBody = body;
    }
}
