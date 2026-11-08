using Core;
using Reqnroll;
using Reqnroll.BoDi;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CleanupRunSteps(FactoryRunSteps factory, IObjectContainer container)
{
    private const string DefaultCheck = "grep -q Install README.md";
    private const string DefaultScope = "README.md";

    private RunHarness H => factory.Harness;

    private string Summary => H.Summary;

    private IEnumerable<(int Index, string[] Before, string[] After)> Execs()
    {
        int index = 0;
        foreach (CommandSpec spec in H.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
        {
            List<string> arguments = [.. spec.Arguments];
            int id = arguments.IndexOf(GateSandbox.ContainerId);
            yield return (index++, [.. arguments[1..id]], [.. arguments[(id + 1)..]]);
        }
    }

    private (int Index, string[] Before, string[] After) Check() =>
        Execs().First(e => e.After is ["sh", "-c", ..]);

    private (int Index, string[] Before, string[] After) Add() =>
        Execs().First(e => e.After is ["git", "worktree", "add", ..]);

    private static string Body(string? scope, string? check) =>
        (scope is null ? "" : $"## Scope\n{scope}\n\n") + (check is null ? "" : $"## Check\n\n```sh\n{check}\n```\n");

    private void SetIssue(string label, string title, string body)
    {
        H.Labels.Add(label);
        H.Title = title;
        H.Body = body;
    }

    private AgentRequest NextBuild() => H.Builder.Of(Phase.Build).ElementAt(1);

    [Given("the issue has the label {string}, the title {string} and a body with the scope {string} and the first fenced block under {string} holding the line {string}")]
    public void GivenIssue(string label, string title, string scope, string heading, string line)
    {
        Assert.Equal("## Check", heading);
        SetIssue(label, title, Body(scope, line));
    }

    [Given("the issue has the labels {string} and {string}")]
    public void GivenLabels(string first, string second)
    {
        H.Labels.Add(first);
        H.Labels.Add(second);
        H.Body = Body(DefaultScope, DefaultCheck);
    }

    [Given("the issue has the label {string}, the title {string} and a body with no heading {string}")]
    public void GivenNoHeading(string label, string title, string heading) =>
        SetIssue(label, title, heading is "## Scope" ? Body(null, DefaultCheck) : Body(DefaultScope, null));

    [Given("the issue has the label {string}, the title {string} and a body whose heading {string} holds only blank lines before the next heading")]
    public void GivenBlank(string label, string title, string heading)
    {
        Assert.Equal("## Scope", heading);
        SetIssue(label, title, "## Scope\n\n   \n\n## Check\n\n```sh\n" + DefaultCheck + "\n```\n");
    }

    [Given("the issue has the label {string}, the title {string} and a body whose heading {string} holds no fenced block before the next heading")]
    public void GivenNoBlock(string label, string title, string heading)
    {
        Assert.Equal("## Check", heading);
        SetIssue(label, title, "## Scope\nREADME.md\n\n## Check\nRun the command by hand.\n\n## Notes\n\n```sh\n" + DefaultCheck + "\n```\n");
    }

    [Given("the check command exits with the code {int} before the build")]
    public void GivenBefore(int code) => H.CheckResults[0] = (code, "", "");

    [Given("the check command exits with the code {int} before the build and writes a standard error")]
    public void GivenBeforeError(int code) => H.CheckResults[0] = (code, "", "sh: 1: cannot run the check");

    [Given("the build changes only {string}")]
    public void GivenChanges(string path)
    {
        H.FirstBuildChanges.Add(path);
        H.CloneFiles.Add(path);
        H.BuildChanges(1, path);
    }

    [Given("the check command then exits with the code {int}")]
    public void GivenThen(int code) => H.CheckResults[1] = (code, "", "");

    [Given("the test {string} passed on the base commit")]
    public void GivenBasePassed(string name)
    {
        int dot = name.LastIndexOf('.');
        H.BaseOnlyTests.Add((name[..dot], name[(dot + 1)..]));
    }

    [Given("the TRX file after the build holds no test of the class {string} and the method {string}")]
    public void GivenLost(string testClass, string method) => Assert.Contains((testClass, method), H.BaseOnlyTests);

    [Given("the TRX file after the build holds a passed test that the base commit lacks")]
    public void GivenNew() => H.AfterOnlyTests.Add(("Specs.Unit.NewTests", "Works"));

    [Given("each test that passed on the base commit passes")]
    public void GivenAllPass() => Assert.Empty(H.BaseOnlyTests);

    [Given("the check command exits with the code {int} after the build and writes a standard output and a standard error")]
    public void GivenAfterRed(int code) => H.CheckResults[1] = (code, "standard output of the check", "standard error of the check");

    [Given("the issue {int} has the label {string} and each step passes")]
    public void GivenConsoleIssue(int number, string label)
    {
        Assert.Equal(12, number);
        SetConsoleIssue(label);
    }

    [Given("the issue {int} has the label {string}")]
    public void GivenConsoleLabel(int number, string label)
    {
        DeliverBodyState body = container.Resolve<DeliverBodyState>();
        if (body.InUse)
        {
            DeliverBodySteps.AddLabel(body, label);
            return;
        }

        if (number is 11 or 15)
        {
            H.Labels.Add(label);
            if (label is "cleanup" or "reframe")
            {
                H.Body = Body(DefaultScope, DefaultCheck);
            }

            return;
        }

        Assert.Equal(12, number);
        SetConsoleIssue(label);
    }

    [Given("the check command exits with the code {int} after the first build and with the code {int} after the second")]
    public void GivenConsoleChecks(int first, int second)
    {
        ConsoleHarness console = container.Resolve<ConsoleProgramSteps>().Harness;
        console.CheckResults[1] = (first, "", "the check is red");
        console.CheckResults[2] = (second, "", "");
    }

    private void SetConsoleIssue(string label)
    {
        ConsoleHarness console = container.Resolve<ConsoleProgramSteps>().Harness;
        console.Labels.Add(label);
        console.IssueBody = Body(DefaultScope, DefaultCheck);
    }

    [Then("FactoryRun runs no prompt with the plan model")]
    public void ThenNoPlan() => Assert.DoesNotContain(H.AgentRequests, r => r.Model == H.PlanModel);

    [Then("FactoryRun starts no container")]
    public void ThenNoContainer() => Assert.DoesNotContain(H.Commands, c => c.Command is "docker");

    [Then("the container runs {string} with the argument {string} before the build prompt")]
    public void ThenShell(string program, string argument)
    {
        string[] words = program.Split(' ');
        (_, _, string[] after) = Check();
        Assert.Equal([.. words, argument], after);
        int check = H.Timeline.IndexOf(string.Join(' ', ["exec", .. after]));
        Assert.True(check >= 0 && check < H.Timeline.IndexOf("prompt build"), string.Join(" | ", H.Timeline));
    }

    [Then("the container runs {string} at a path outside {string} and the check command runs in that path on the head commit")]
    public void ThenWorktree(string words, string root)
    {
        (_, _, string[] after) = Add();
        Assert.Equal(words.Split(' '), after[..^2]);
        Assert.Equal("HEAD", after[^1]);
        Assert.DoesNotContain(root, after[^2], StringComparison.Ordinal);
        Assert.StartsWith("/", after[^2], StringComparison.Ordinal);
        Assert.Equal(["--workdir", after[^2]], Check().Before);
    }

    [Then("the container runs {string} on the copy after the check command ends")]
    public void ThenRemoves(string words)
    {
        string copy = Add().After[^2];
        (int index, _, string[] after) = Execs().First(e => e.After is ["git", "worktree", "remove", ..]);
        Assert.Equal([.. words.Split(' '), copy], after);
        Assert.True(index > Check().Index);
    }

    [Then("FactoryRun posts one comment on the issue that holds {string} and the standard error of the command")]
    public void ThenCommentError(string text)
    {
        string body = Assert.Single(H.CommentBodies);
        Assert.Contains(text, body, StringComparison.Ordinal);
        Assert.Contains("sh: 1: cannot run the check", body, StringComparison.Ordinal);
    }

    [Then("FactoryRun runs one prompt with the model {string} and the turn limit {int} that holds the title and the body of the issue")]
    public void ThenBuildPrompt(string model, int turns)
    {
        AgentRequest prompt = Assert.Single(H.AgentRequests, r => r.Model == model);
        Assert.Equal(turns, prompt.MaxTurns);
        Assert.Contains(H.Title, prompt.Prompt.Text, StringComparison.Ordinal);
        Assert.Contains(H.Body, prompt.Prompt.Text, StringComparison.Ordinal);
    }

    [Then("the build prompt holds no {string}")]
    public void ThenBuildNo(string text) => Assert.DoesNotContain(text, Assert.Single(H.Builder.Of(Phase.Build)).Prompt.Text, StringComparison.Ordinal);

    [Then("the review prompt holds no {string}")]
    public void ThenReviewNo(string text) => Assert.DoesNotContain(text, Assert.Single(H.Reviewer.Of(Phase.Review)).Prompt.Text, StringComparison.Ordinal);

    [Then("the next build prompt holds a Redo text that holds {string}")]
    public void ThenRedo(string text) => Assert.Contains(text, NextBuild().Prompt.Text, StringComparison.Ordinal);

    [Then("the next build prompt holds a Redo text that holds {string}, the standard output and the standard error of the command")]
    public void ThenRedoOutput(string code)
    {
        string text = NextBuild().Prompt.Text;
        Assert.Contains(code, text, StringComparison.Ordinal);
        Assert.Contains("standard output of the check", text, StringComparison.Ordinal);
        Assert.Contains("standard error of the check", text, StringComparison.Ordinal);
    }

    [Then("the job summary holds the row {string} with the result {string}")]
    public void ThenSummaryRow(string name, string result)
    {
        string row = Summary.Split('\n').Single(line => line.StartsWith($"| {name} |", StringComparison.Ordinal));
        Assert.Equal(result, row.Trim('|').Split('|')[4].Trim());
    }

    [Then("the Redos list of the job summary holds the item {string}")]
    public void ThenSummaryRedo(string item) => Assert.Contains(item, Summary.Split('\n'));

    [Then("no review prompt runs before it")]
    public void ThenNoReviewBefore()
    {
        List<AgentRequest> prompts = H.AgentRequests;
        int next = prompts.IndexOf(NextBuild());
        Assert.True(next >= 0);
        Assert.DoesNotContain(prompts.Take(next), r => r.Phase is Phase.Review);
    }

    [Then("FactoryRun runs the review prompt")]
    public void ThenReview() => Assert.Single(H.Reviewer.Of(Phase.Review));

    [Then("FactoryRun runs one prompt with the model {string} and the turn limit {int} that holds the title of the issue, the body of the issue and the base commit")]
    public void ThenReviewPrompt(string model, int turns)
    {
        AgentRequest prompt = Assert.Single(H.AgentRequests, r => r.Model == model);
        Assert.Equal(turns, prompt.MaxTurns);
        Assert.Contains(H.Title, prompt.Prompt.Text, StringComparison.Ordinal);
        Assert.Contains(H.Body, prompt.Prompt.Text, StringComparison.Ordinal);
        Assert.Contains(H.HeadSha, prompt.Prompt.Text, StringComparison.Ordinal);
    }

    [Then("FactoryRun opens a pull request with the title {string} and gives back a result with that pull request and {int} Redos")]
    public void ThenOpens(string title, int redos)
    {
        SeenRequest open = Assert.Single(H.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));
        using var json = System.Text.Json.JsonDocument.Parse(open.Content!);
        Assert.Equal(title, json.RootElement.GetProperty("title").GetString());
        Assert.NotNull(FactoryResult().Passed);
        Assert.Equal(redos, FactoryResult().Redos);
    }

    [Then("standard output holds the lines in this order with no line between them")]
    public void ThenConsecutive(Table table)
    {
        List<string> lines = [.. ConsoleHarness.Lines(container.Resolve<ConsoleProgramSteps>().Harness.Out)];
        List<string> wanted = [.. table.Rows.Select(r => r[0])];
        Assert.Contains(
            Enumerable.Range(0, Math.Max(0, lines.Count - wanted.Count + 1)),
            at => lines.Skip(at).Take(wanted.Count).SequenceEqual(wanted));
    }

    private RunOutcome<Delivered> FactoryResult() => factory.LastResult;
}
