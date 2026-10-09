using System.Text.Json;
using Core;
using Reqnroll;
using Reqnroll.BoDi;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class ReframeRunSteps(FactoryRunSteps factory, IObjectContainer container)
{
    private const string Printer = "/opt/api-printer/ApiPrinter.dll";
    private const string CopyBuildOutput = "error CS1002: ; expected";

    private FactoryRunHarness H => factory.Harness;

    /// <summary>Each docker exec of the run: its index, the words before the container id and the words after it.</summary>
    private IEnumerable<(int Index, string[] Before, string[] After)> Execs()
    {
        int index = 0;
        foreach (CommandSpec spec in H.Commands.Where(c => c.Command is "docker" && c.Arguments[0] is "exec"))
        {
            List<string> arguments = [.. spec.Arguments];
            int id = arguments.IndexOf(FactoryRunHarness.ContainerId);
            yield return (index++, [.. arguments[1..id]], [.. arguments[(id + 1)..]]);
        }
    }

    private (int Index, string[] Before, string[] After) FirstPrinter() =>
        Execs().First(e => e.After is ["dotnet", Printer, ..]);

    private static string? Workdir((int Index, string[] Before, string[] After) exec)
    {
        int at = Array.IndexOf(exec.Before, "--workdir");
        return at < 0 ? null : exec.Before[at + 1];
    }

    private SeenPrompt NextBuild() => H.Prompts.Where(p => p.Kind is "build").ElementAt(1);

    [Given("the API printer prints the same lines as on the base commit")]
    public void GivenSameApi() => Assert.Null(H.AfterApi);

    [Given("the API printer prints the line {string} on the base commit")]
    public void GivenBaseLine(string line) => Assert.Contains(line, H.BaseApi);

    [Given("the API printer prints no line {string} on the base commit")]
    public void GivenNoBaseLine(string line) => Assert.DoesNotContain(line, H.BaseApi);

    [Given("after the build the API printer prints every other line of the base commit and no such line")]
    public void GivenLostLine() => H.AfterApi = [.. H.BaseApi.Where(line => line != H.BaseApi[0])];

    [Given("after the build the API printer prints each line of the base commit and the line {string}")]
    public void GivenNewLine(string line) => H.AfterApi = [.. H.BaseApi, line];

    [Given("the API printer exits with the code {int} before the build and writes the standard error {string}")]
    public void GivenPrinterFailsBefore(int code, string error) => H.ApiResults[0] = (code, error);

    [Given("the API printer exits with the code {int} after the build and writes the standard error {string}")]
    public void GivenPrinterFailsAfter(int code, string error) => H.ApiResults[1] = (code, error);

    [Given("dotnet build in the copy exits with the code {int} before the build and writes a standard output")]
    public void GivenCopyBuildFails(int code) => H.CopyBuildResults[0] = (code, CopyBuildOutput);

    [Given("the API printer of the program prints no line {string} on the base commit, and after the first build it prints that line and each line of the base commit")]
    public void GivenConsoleApi(string line)
    {
        ConsoleHarness console = container.Resolve<ConsoleProgramSteps>().Harness;
        const string Base = "Core: class static Core.Greeter\n";
        console.ApiOutputs[0] = Base;
        console.ApiOutputs[1] = Base + line + "\n";
        console.ApiOutputs[2] = Base;
    }

    [Then("the container runs {string} with the arguments {string} and {string} before the build prompt")]
    public void ThenPrinterBeforeBuild(string program, string first, string second)
    {
        (int index, _, string[] after) = FirstPrinter();
        Assert.Equal([program, first, second], after);
        (int build, _, _) = Execs().First(e => e.After[0] is "claude");
        Assert.True(index < build);
    }

    [Then("the container runs {string} at a path outside {string}, and in that path {string} and then {string} run before the API printer")]
    public void ThenPrinterWorktree(string words, string root, string restore, string build)
    {
        (int printer, _, _) = FirstPrinter();
        string copy = Workdir(FirstPrinter()) ?? throw new InvalidOperationException("the API printer ran with no --workdir");
        Assert.DoesNotContain(root, copy, StringComparison.Ordinal);
        Assert.StartsWith("/", copy, StringComparison.Ordinal);
        (int add, _, string[] added) = Execs().First(e => e.After is ["git", "worktree", "add", ..] && e.After[^2] == copy);
        Assert.Equal([.. words.Split(' '), copy, "HEAD"], added);
        List<(int Index, string[] Before, string[] After)> inCopy = [.. Execs().Where(e => Workdir(e) == copy && e.After[0] is "dotnet")];
        Assert.Equal([restore, build, $"dotnet {Printer} src"], inCopy.Select(e => string.Join(' ', e.After)));
        Assert.True(add < inCopy[0].Index && inCopy[1].Index < printer);
    }

    [Then("the container runs {string} on the copy after the API printer ends")]
    public void ThenPrinterCopyRemoved(string words)
    {
        (int printer, _, _) = FirstPrinter();
        string copy = Workdir(FirstPrinter())!;
        (int index, _, string[] after) = Execs().First(e => e.After is ["git", "worktree", "remove", ..] && e.After[^1] == copy);
        Assert.Equal([.. words.Split(' '), copy], after);
        Assert.True(index > printer);
    }

    [Then("FactoryRun posts one comment on the issue that holds {string} and the standard output of the copy's build")]
    public void ThenCommentCopyBuild(string text)
    {
        string body = Assert.Single(H.CommentBodies);
        Assert.Contains(text, body, StringComparison.Ordinal);
        Assert.Contains(CopyBuildOutput, body, StringComparison.Ordinal);
    }

    [Then("FactoryRun runs no API printer")]
    public void ThenNoPrinter() => Assert.DoesNotContain(Execs(), e => e.After is ["dotnet", Printer, ..]);

    [Then("FactoryRun posts one comment on the issue that holds {string} and the standard error of the API printer")]
    public void ThenCommentPrinter(string text)
    {
        string body = Assert.Single(H.CommentBodies);
        Assert.Contains(text, body, StringComparison.Ordinal);
        Assert.Contains(H.ApiResults[0].Err, body, StringComparison.Ordinal);
    }

    [Then("the build prompt holds this text")]
    public void ThenBuildText(string docString) =>
        Assert.Contains(docString.Trim(), Assert.Single(H.Prompts, p => p.Kind is "build").Text, StringComparison.Ordinal);

    [Then("the review prompt holds this text")]
    public void ThenReviewText(string docString) =>
        Assert.Contains(docString.Trim(), Assert.Single(H.Prompts, p => p.Kind is "review").Text, StringComparison.Ordinal);

    [Then("the next build prompt holds {string}")]
    public void ThenNextBuildHolds(string text) => Assert.Contains(text, NextBuild().Text, StringComparison.Ordinal);

    [Then("the next build prompt holds a Redo text with {string} once, in {string}, and no {string}")]
    public void ThenSymbolRedo(string marker, string line, string absent)
    {
        string text = NextBuild().Text;
        Assert.Contains(line, text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split(marker).Length - 1);
        Assert.DoesNotContain(absent, text, StringComparison.Ordinal);
    }

    [Then("the next build prompt holds a Redo text that holds {string} and the standard error of the API printer")]
    public void ThenPrinterRedo(string text)
    {
        string prompt = NextBuild().Text;
        Assert.Contains(text, prompt, StringComparison.Ordinal);
        Assert.Contains(H.ApiResults[1].Err, prompt, StringComparison.Ordinal);
    }

    [Then("the body of the pull request holds the line {string}")]
    public void ThenPullBody(string line)
    {
        SeenRequest open = Assert.Single(H.Requests, r => r.Method is "POST" && r.Uri.EndsWith("/pulls", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(open.Content!);
        Assert.Contains(line + "\n", json.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    [Then("the section holds, after the paragraph that starts {string}, this paragraph")]
    public static void ThenParagraph(string start, string docString)
    {
        string section = WorkflowChecks.Section(Path.Combine(StarterChecks.RepoRoot(), "README.md"), "## Start a new repo");
        string[] paragraphs = section.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        int at = Array.FindIndex(paragraphs, p => p.StartsWith(start, StringComparison.Ordinal));
        Assert.True(at >= 0);
        Assert.Equal(docString.Trim(), paragraphs[at + 1].Trim());
    }

    [Then("the file {string} of this repo holds this text")]
    public static void ThenRepoFile(string file, string docString) =>
        StarterChecks.TextEquals(Path.Combine(StarterChecks.RepoRoot(), file), docString);
}
