using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FeedbackCleanupScopeSteps(FactoryRunSteps factory, FeedbackRunSteps feedback)
{
    private FeedbackRunHarness H => feedback.Harness;

    private static string ReadmeSection(string heading) =>
        WorkflowChecks.Section(Path.Combine(StarterChecks.RepoRoot(), "README.md"), heading);

    private IEnumerable<string> StdoutLines => H.Out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private IEnumerable<string[]> ShellCommands() =>
        H.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec")
            .Select(c => c.Arguments.SkipWhile(a => a != FeedbackRunHarness.ContainerId).Skip(1).ToArray())
            .Where(a => a is ["sh", "-c", ..]);

    [Given("GitHub answers the read of the pull request with the body {string}")]
    public void GivenBody(string body) => H.PullBody = body;

    [Given("GitHub answers the read of the pull request with a body that holds no closing line")]
    public void GivenNoClosing() => H.PullBody = "Answers the review of the cleanup.";

    [Given("the issue {int} has the label {string} and the scope {string}")]
    public void GivenScope(int number, string label, string scope)
    {
        Assert.Equal(11, number);
        factory.Harness.Labels.Add(label);
        factory.Harness.Body = $"## Scope\n{scope}\n";
    }

    [Given("the issue {int} has the label {string} and the first fenced block under {string} holding the line {string}")]
    public void GivenFenced(int number, string label, string heading, string line) => SetCheck(number, label, heading, line);

    [Given("the issue {int} has the label {string} and the line {string} under {string}")]
    public void GivenLine(int number, string label, string line, string heading) => SetCheck(number, label, heading, line);

    [Given("the issue {int} has no label {string} and the line {string} under {string}")]
    public void GivenNoLabel(int number, string label, string line, string heading)
    {
        Assert.Equal("cleanup", label);
        SetCheck(number, "documentation", heading, line);
    }

    [Given("the test {string} passed on the head of the pull request")]
    public void GivenPassed(string name)
    {
        int dot = name.LastIndexOf('.');
        factory.Harness.BaseOnlyTests.Add((name[..dot], name[(dot + 1)..]));
    }

    [Given("the check command exits with the code {int} after the build")]
    public void GivenCheck(int code) => factory.Harness.CheckResults[1] = (code, "", "");

    [Then("the next build prompt holds a Redo text that holds {string} and the name {string}")]
    public void ThenRedoName(string text, string name)
    {
        string prompt = H.BuildPrompts.ElementAt(1).Text;
        Assert.Contains(text, prompt);
        Assert.Contains(name, prompt);
    }

    [Then("the test gate of the feedback run passes")]
    public void ThenGatePasses()
    {
        Assert.Contains("test: passed", StdoutLines);
        Assert.DoesNotContain("test: failed", StdoutLines);
    }

    [Then("the feedback run runs {string} under sh in a copy of the work tree after the test gate passes")]
    public void ThenCheckRuns(string command)
    {
        Assert.Equal(["sh", "-c", command], Assert.Single(ShellCommands()));
        List<string> lines = [.. StdoutLines];
        Assert.True(lines.IndexOf("test: passed") >= 0);
        Assert.True(lines.IndexOf("check: start") > lines.IndexOf("test: passed"));
        Assert.Contains(H.Commands, c => c.Arguments.Contains("worktree") && c.Arguments.Contains("add"));
    }

    [Then("the feedback run starts the review")]
    public void ThenReview() => Assert.Contains(H.Prompts, p => p.Kind is "review");

    [Then("standard output holds the lines in this order")]
    public void ThenOrder(Table table)
    {
        List<string> lines = [.. StdoutLines];
        int at = -1;
        foreach (string line in table.Rows.Select(r => r[0]))
        {
            int next = lines.IndexOf(line, at + 1);
            Assert.True(next > at, $"the line {line} is absent or out of order");
            at = next;
        }
    }

    [Then("the feedback run runs no check command")]
    public void ThenNoCheck()
    {
        Assert.Empty(ShellCommands());
        Assert.DoesNotContain(StdoutLines, l => l.StartsWith("check:", StringComparison.Ordinal));
    }

    [Then("the feedback run reads no issue")]
    public void ThenNoIssue() => Assert.DoesNotContain(H.Requests, r => r.Uri.Contains("/issues/", StringComparison.Ordinal));

    [Then("the section holds a sentence after the paragraph that starts {string}")]
    public static void ThenSentenceAfter(string start)
    {
        string[] paragraphs = ReadmeSection("## Start a new repo").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        int at = Array.FindIndex(paragraphs, p => p.StartsWith(start, StringComparison.Ordinal));
        Assert.True(at >= 0);
        Assert.True(at + 1 < paragraphs.Length);
        Assert.False(string.IsNullOrWhiteSpace(paragraphs[at + 1]));
    }

    [Then("the sentence states that a feedback run on the pull request of a `cleanup` issue holds the build to the issue's `## Scope` and runs its `## Check`")]
    public static void ThenSentenceStates()
    {
        string[] paragraphs = ReadmeSection("## Start a new repo").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        int at = Array.FindIndex(paragraphs, p => p.StartsWith("The body of a `cleanup` issue names the paths", StringComparison.Ordinal));
        string sentence = paragraphs[at + 1];
        Assert.Contains("feedback run", sentence);
        Assert.Contains("pull request of a `cleanup` issue", sentence);
        Assert.Contains("issue's `## Scope`", sentence);
        Assert.Contains("`## Check`", sentence);
    }

    private void SetCheck(int number, string label, string heading, string line)
    {
        Assert.Equal(11, number);
        Assert.Equal("## Check", heading);
        factory.Harness.Labels.Add(label);
        factory.Harness.Body = $"## Check\n\n```sh\n{line}\n```\n";
    }
}
