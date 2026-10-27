using System.Globalization;
using Reqnroll;
using Reqnroll.BoDi;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class RefactorRunSteps(FactoryRunSteps factory, IObjectContainer container)
{
    private const string CheckError = "sh: 1: cannot run the check";

    private FactoryRunHarness H => factory.Harness;

    private IEnumerable<string[]> Execs() =>
        H.Commands
            .Where(c => c.Command is "docker" && c.Arguments[0] is "exec")
            .Select(spec =>
            {
                List<string> arguments = [.. spec.Arguments];
                return arguments[(arguments.IndexOf(FactoryRunHarness.ContainerId) + 1)..].ToArray();
            });

    private static string CheckBody(string line) => $"## Check\n\n```sh\n{line}\n```\n";

    [Given("the issue has the label {string}, the title {string} and a body with the first fenced block under {string} holding the line {string}")]
    public void GivenIssue(string label, string title, string heading, string line)
    {
        Assert.Equal("## Check", heading);
        H.Labels.Add(label);
        H.Title = title;
        H.Body = CheckBody(line);
    }

    [Given("the check command exits with the code {int} before the plan")]
    public void GivenBefore(int code) => H.CheckResults[0] = (code, "", "");

    [Given("the check command exits with the code {int} before the plan and writes a standard error")]
    public void GivenBeforeError(int code) => H.CheckResults[0] = (code, "", CheckError);

    [Given("the agent on the plan prompt writes {string} and changes no other file")]
    public void GivenPlanOnly(string path)
    {
        Assert.Equal(".factory/plan.md", path);
        Assert.True(H.AgentWritesPlan);
        Assert.Empty(H.PlanExtraFiles);
    }

    [Given("the agent on the first plan prompt writes {string} and the file {string}")]
    public void GivenPlanExtra(string path, string extra)
    {
        Assert.Equal(".factory/plan.md", path);
        H.PlanExtraFiles.Add(extra);
    }

    [Given("the agent on each plan prompt leaves {string} absent")]
    public void GivenNoPlan(string path)
    {
        Assert.Equal(".factory/plan.md", path);
        H.AgentWritesPlan = false;
    }

    [Given("the build changes only {string} and {string}")]
    public void GivenChangesTwo(string first, string second)
    {
        H.FirstBuildChanges.Add(first);
        H.FirstBuildChanges.Add(second);
    }

    [Given("the build adds only the file {string}")]
    public void GivenAdds(string path) => H.FirstBuildAdds.Add(path);

    [Given("the build removes only the file {string}")]
    public void GivenRemoves(string path) => H.FirstBuildRemoves.Add(path);

    [Given("the issue {int} has the label {string} and a {string} and each step passes")]
    public void GivenConsoleWithCheck(int number, string label, string heading)
    {
        Assert.Equal("## Check", heading);
        SetConsoleIssue(number, label, CheckBody("test -f src/Core/Greeting.cs"));
    }

    [Given("the issue {int} has the label {string} and no {string} and each step passes")]
    public void GivenConsoleNoCheck(int number, string label, string heading)
    {
        Assert.Equal("## Check", heading);
        SetConsoleIssue(number, label, "Rename Greeting to Greeter.");
    }

    [Then("the container runs no {string} command")]
    public void ThenNoCommand(string program) =>
        Assert.DoesNotContain(Execs(), after => string.Join(' ', after.Take(program.Split(' ').Length)) == program);

    [Then("FactoryRun runs one prompt with the model {string}")]
    public void ThenOnePrompt(string model) => Assert.Single(H.Prompts, p => p.Model == model);

    [Then("the container runs {string} with the argument {string} before the plan prompt")]
    public void ThenShell(string program, string argument)
    {
        List<string[]> execs = [.. Execs()];
        int check = execs.FindIndex(after => after is ["sh", "-c", ..]);
        Assert.True(check >= 0);
        Assert.Equal([.. program.Split(' '), argument], execs[check]);
        Assert.True(check < execs.FindIndex(after => after[0] is "claude"));
    }

    [Then("FactoryRun runs no plan prompt")]
    public void ThenNoPlan() => Assert.DoesNotContain(H.Prompts, p => p.Kind is "plan");

    [Then("FactoryRun runs one prompt with the model {string} and the turn limit {int} that holds the title of the issue, the body of the issue and {string}")]
    public void ThenPromptHolds(string model, int turns, string path)
    {
        SeenPrompt prompt = Assert.Single(H.Prompts, p => p.Model == model);
        Assert.Equal(turns.ToString(CultureInfo.InvariantCulture), prompt.Turns);
        Assert.Contains(H.Title, prompt.Text, StringComparison.Ordinal);
        Assert.Contains(H.Body, prompt.Text, StringComparison.Ordinal);
        Assert.Contains(path, prompt.Text, StringComparison.Ordinal);
    }

    [Then("FactoryRun runs one prompt with the model {string} and the turn limit {int} that holds the title of the issue, the body of the issue, the base commit and {string}")]
    public void ThenReviewHolds(string model, int turns, string path)
    {
        ThenPromptHolds(model, turns, path);
        Assert.Contains(H.HeadSha, Assert.Single(H.Prompts, p => p.Model == model).Text, StringComparison.Ordinal);
    }

    [Then("the plan prompt holds no {string}")]
    public void ThenPlanNo(string text) => Assert.DoesNotContain(text, Assert.Single(H.Prompts, p => p.Kind is "plan").Text, StringComparison.Ordinal);

    [Then("the second plan prompt holds {string}")]
    public void ThenSecondPlan(string text) => Assert.Contains(text, H.Prompts.Where(p => p.Kind is "plan").ElementAt(1).Text, StringComparison.Ordinal);

    private void SetConsoleIssue(int number, string label, string body)
    {
        Assert.Equal(12, number);
        ConsoleHarness console = container.Resolve<ConsoleProgramSteps>().Harness;
        console.Labels.Add(label);
        console.IssueBody = body;
    }
}
