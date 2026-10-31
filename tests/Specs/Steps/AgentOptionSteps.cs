using System.Text.Json;
using System.Text.RegularExpressions;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed partial class AgentOptionSteps(ConsoleProgramSteps console)
{
    private ConsoleHarness H => console.Harness;

    private List<string> Pushes => [.. H.Commands.Where(c => c.Command is "git" && c.Arguments[0] is "push").Select(c => string.Join(' ', c.Arguments))];

    private string DeliveryBranch => H.Commands
        .Where(c => c.Command is "git" && c.Arguments is ["push", "origin", _])
        .Select(c => c.Arguments[2])
        .Single(name => DeliveryName().IsMatch(name));

    private string PullBody =>
        JsonDocument.Parse(Assert.Single(H.Posts("/pulls")).Content!).RootElement.GetProperty("body").GetString() ?? "";

    [GeneratedRegex("^factory/[0-9a-f]{8}$")]
    private static partial Regex DeliveryName();

    [Given("the environment holds the variables of the managed agent")]
    public void GivenManaged() => H.UseManaged();

    [Given("a session answer costs {int} cents for the plan, {int} cents for a build and {int} cents for a review")]
    public void GivenCents(int plan, int build, int review)
    {
        H.SessionCents["plan"] = plan;
        H.SessionCents["build"] = build;
        H.SessionCents["review"] = review;
    }

    [Given("the environment variable GITHUB_STEP_SUMMARY of the program names an empty file")]
    public void GivenSummary()
    {
        string path = Path.Combine(H.Root, "summary.md");
        File.WriteAllText(path, "");
        H.Environment["GITHUB_STEP_SUMMARY"] = path;
    }

    [Then("docker runs a command")]
    public void ThenDocker() => Assert.Contains(H.Commands, c => c.Command is "docker");

    [Then("the docker run of the program holds neither {string} nor {string}")]
    public void ThenDockerRunLacks(string first, string second)
    {
        CommandSpec run = Assert.Single(H.Commands, c => c.Command is "docker" && c.Arguments[0] is "run");
        Assert.DoesNotContain(first, run.Arguments);
        Assert.DoesNotContain(second, run.Arguments);
    }

    [Then("git pushes the session branch, the name of the delivery branch with {string} after it, before the first session starts")]
    public void ThenSessionBranchFirst(string suffix)
    {
        string session = DeliveryBranch + suffix;
        int push = H.Timeline.IndexOf($"git push origin {session}");
        int start = H.Timeline.FindIndex(line => line is "POST /v1/sessions");
        Assert.True(push >= 0, $"no push of {session}: {string.Join(" | ", Pushes)}");
        Assert.True(start >= 0, "no session started");
        Assert.True(push < start, "the session started before the push of its branch");
    }

    [Then("git deletes the session branch on origin after the push of the delivery branch")]
    public void ThenSessionBranchDeleted()
    {
        string delivery = DeliveryBranch;
        string session = delivery + "-session";
        int deliveryPush = H.Timeline.IndexOf($"git push origin {delivery}");
        int delete = H.Timeline.FindLastIndex(line => line.StartsWith("git push", StringComparison.Ordinal)
            && line.Contains("--delete", StringComparison.Ordinal) && line.EndsWith(" " + session, StringComparison.Ordinal));
        Assert.True(delete > deliveryPush, $"pushes: {string.Join(" | ", Pushes)}");
    }

    [Then("the container runs {string} before {string}")]
    public void ThenContainerOrder(string first, string second)
    {
        List<string> container =
        [
            .. H.Commands
                .Where(c => c.Command is "docker" && c.Arguments[0] is "exec" && c.Arguments.Contains("dotnet"))
                .Select(c => string.Join(' ', c.Arguments.SkipWhile(argument => argument is not "dotnet"))),
        ];
        int a = container.IndexOf(first);
        int b = container.IndexOf(second);
        Assert.True(a >= 0 && b > a, $"container commands: {string.Join(" | ", container)}");
    }

    [Then("the body of the pull request holds a line that starts with {string}")]
    public void ThenBodyLine(string start) => Assert.Contains(PullBody.Split('\n'), line => line.StartsWith(start, StringComparison.Ordinal));

    [Then("the body of the pull request ends with the line {string}")]
    public void ThenBodyEnds(string line) => Assert.Equal(line, PullBody.Split('\n')[^1]);

    [Then("the job summary of the program ends with the lines")]
    public void ThenSummaryEnds(string expected) =>
        Assert.EndsWith(
            expected.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n",
            File.ReadAllText(H.Environment["GITHUB_STEP_SUMMARY"]),
            StringComparison.Ordinal);
}
