using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class RunLogSteps(ConsoleProgramSteps console)
{
    private const string CleanupBody = "Tidy the code.\n\n## Scope\n\nsrc/\n\n## Check\n\n```\ntrue\n```\n";

    private ConsoleHarness H => console.Harness;

    [Given("every dotnet command in the container exits with the code {int}")]
    public static void GivenEveryDotnet(int code) => Assert.Equal(0, code);

    [Given("the dotnet command {string} exits with the code {int}")]
    public void GivenDotnetExit(string command, int code) => H.DotnetExits[command] = code;

    [Given("the issue has the label {string} and the check exits with the code {int}")]
    public void GivenCleanup(string label, int code)
    {
        H.Labels.Add(label);
        H.IssueBody = CleanupBody;
        H.CheckResults[0] = (code, "", "");
    }

    [Given("the issue has the label {string} and a {string} and the check exits with the code {int}")]
    public void GivenRefactor(string label, string heading, int code)
    {
        H.Labels.Add(label);
        H.IssueBody = $"Tidy the code.\n\n{heading}\n\n```\ntrue\n```\n";
        H.CheckResults[0] = (code, "", "");
    }

    [Then("standard error holds these command lines in this order")]
    public void ThenCommandLines(Table table)
    {
        List<string> lines = [.. ConsoleHarness.Lines(H.Error)];
        int at = 0;
        foreach (DataTableRow? row in table.Rows)
        {
            string[] parts = row[0].Split("<any>");
            int found = lines.FindIndex(at, l => l.StartsWith(parts[0], StringComparison.Ordinal) && l.EndsWith(parts[^1], StringComparison.Ordinal));
            Assert.True(found >= 0, $"no line {row[0]} after line {at}");
            at = found + 1;
        }
    }
}
