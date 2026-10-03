using System.Text.Json;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ProgressLinesSteps(ConsoleProgramSteps console)
{
    private ConsoleHarness H => console.Harness;

    private List<string> Lines => [.. ConsoleHarness.Lines(H.Out)];

    [Given("the agent writes only {string} on the plan prompt")]
    public void GivenOnlyPlan(string path)
    {
        Assert.Equal(".factory/plan.md", path);
        H.AgentWritesOnlyPlan = true;
    }

    [Given("the test gate is red after the first build and green after the second")]
    public void GivenRedFirstGreenSecond()
    {
        H.RedBuilds.Clear();
        H.RedBuilds.Add(1);
    }

    [Given("the agent writes a blocked review on the first review prompt")]
    public void GivenBlockedReview() => H.BlockedReviews = 1;

    [Then("standard output holds exactly these lines in this order")]
    public void ThenExactLines(Table table)
    {
        List<string> expected = [table.Header.First(), .. table.Rows.Select(row => row[0])];
        Assert.Equal(expected.Skip(1), Lines);
    }

    [Then("when docker gets the command {string} standard output already holds the line {string}")]
    public void ThenDockerSeesLine(string command, string line)
    {
        int index = H.Commands.FindIndex(c => c.Command is "docker" && string.Join(' ', c.Arguments).StartsWith(command, StringComparison.Ordinal));
        Assert.True(index >= 0);
        Assert.Contains(line, H.OutAtCommand[index].Split('\n'));
    }

    [Then("when claude gets the plan prompt standard output already holds the line {string}")]
    public void ThenClaudeSeesLine(string line)
    {
        int index = H.Prompts.FindIndex(p => p.Kind is "plan");
        Assert.True(index >= 0);
        Assert.Contains(line, H.OutAtPrompt[index].Split('\n'));
    }

    [Then("after that line standard output holds the body of the comment that GitHub gets on the issue {int} and that body ends standard output")]
    public void ThenBodyAfterLine(int number)
    {
        string line = Assert.Single(Lines, l => l is "layout: failed" or "plan: failed");
        AssertBodyEnds(number, LastLineEnd(line));
    }

    [Then("after the last line {string} standard output holds the body of the comment that GitHub gets on the issue {int} and that body ends standard output")]
    public void ThenBodyAfterLast(string line, int number) => AssertBodyEnds(number, LastLineEnd(line));

    [Then("standard output holds the lines {string}, {string}, {string} in this order with no line between them")]
    public void ThenContiguous(string a, string b, string c)
    {
        List<string> lines = Lines;
        bool found = false;
        for (int i = 0; i + 2 < lines.Count; i++)
        {
            found |= lines[i] == a && lines[i + 1] == b && lines[i + 2] == c;
        }

        Assert.True(found, H.Out.ToString());
    }

    [Then("standard output holds each of the lines {string}, {string} and {string} one time")]
    public void ThenEachOnce(string a, string b, string c)
    {
        foreach (string line in new[] { a, b, c })
        {
            Assert.Single(Lines, l => l == line);
        }
    }

    [Then("standard output holds the line {string} one time, between the first and the second line {string}")]
    public void ThenOnceBetween(string line, string around)
    {
        List<string> lines = Lines;
        Assert.Single(lines, l => l == line);
        List<int> bounds = [.. lines.Select((l, i) => (l, i)).Where(x => x.l == around).Select(x => x.i)];
        Assert.True(bounds.Count >= 2);
        int at = lines.IndexOf(line);
        Assert.True(bounds[0] < at && at < bounds[1]);
    }

    [Then("standard output holds the body of the comment that GitHub gets on the pull request {int} between the last line {string} and the line {string}")]
    public void ThenBodyBetween(int number, string last, string next)
    {
        string output = H.Out.ToString();
        string body = Body(number);
        int start = LastLineEnd(last);
        int end = output.IndexOf(next + "\n", start, StringComparison.Ordinal);
        Assert.True(end >= 0);
        int at = output.IndexOf(body, start, StringComparison.Ordinal);
        Assert.True(at >= start && at + body.Length <= end, output);
    }

    private string Body(int number)
    {
        TimedRequest post = Assert.Single(H.Posts($"/issues/{number}/comments"));
        using JsonDocument json = JsonDocument.Parse(post.Content!);
        return json.RootElement.GetProperty("body").GetString()!;
    }

    private int LastLineEnd(string line)
    {
        string padded = "\n" + H.Out;
        int found = padded.LastIndexOf("\n" + line + "\n", StringComparison.Ordinal);
        Assert.True(found >= 0, $"No line {line}");
        return found + line.Length + 1;
    }

    private void AssertBodyEnds(int number, int after)
    {
        string output = H.Out.ToString();
        string body = Body(number).TrimEnd('\n');
        Assert.EndsWith(body, output.TrimEnd('\n'), StringComparison.Ordinal);
        Assert.True(output.TrimEnd('\n').Length - body.Length >= after, output);
    }
}
