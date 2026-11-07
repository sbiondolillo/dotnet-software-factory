using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CheckGateTests
{
    private const string Command = "grep -q Install README.md";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GateSandbox Answering(int code, string output = "", string error = "") =>
        new() { Answer = words => words is ["sh", "-c", Command] ? new CommandResult { ExitCode = code, StandardOutput = output, StandardError = error } : new CommandResult() };

    [Fact]
    public async Task ACheckThatExitsWithTheCodeZeroIsGreenWithNoRedoText()
    {
        await using GateSandbox box = Answering(0);
        var gate = new CheckGate(await box.StartAsync(Ct), Command);
        GateOutcome outcome = await GateSandbox.RunAsync(gate, Ct);
        Assert.Equal(GateName.Check, GateSandbox.NameOf(gate));
        Assert.Equal("green", outcome.Result);
        Assert.Empty(outcome.RedoTexts);
        Assert.Contains(box.Seen, words => words is ["git", "worktree", "remove", ..]);
    }

    [Fact]
    public async Task ACheckThatFailsIsRedWithItsCodeAndBothOutputs()
    {
        await using GateSandbox box = Answering(2, "the out", "the err");
        var gate = new CheckGate(await box.StartAsync(Ct), Command);
        GateOutcome outcome = await GateSandbox.RunAsync(gate, Ct);
        Assert.Equal("red", outcome.Result);
        Assert.Equal(["The check command exited with the code 2\nstandard output: the out\nstandard error: the err"], outcome.RedoTexts);
    }

    [Theory]
    [InlineData("before the plan", "refactor", "The check passes before the plan, so there is nothing to refactor.")]
    [InlineData("before the build", "clean up", "The check passes before the build, so there is nothing to clean up.")]
    public async Task APassingPreCheckGivesTheStopTextOfItsWords(string when, string work, string stop)
    {
        await using GateSandbox box = Answering(0);
        using var progress = new StringWriter();
        string? text = await CheckGate.PreCheckAsync(await box.StartAsync(Ct), Command, new PreCheckWords(when, work), progress, cancellationToken: Ct);
        Assert.Equal(stop, text);
        Assert.Equal(["check: start", "check: green"], progress.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task APreCheckThatExitsWithTheCodeOneGivesNoStopText()
    {
        await using GateSandbox box = Answering(1);
        using var progress = new StringWriter();
        Assert.Null(await CheckGate.PreCheckAsync(await box.StartAsync(Ct), Command, new PreCheckWords("before the plan", "refactor"), progress, cancellationToken: Ct));
        Assert.Equal(["check: start", "check: red"], progress.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData(126, "before the plan")]
    [InlineData(127, "before the plan")]
    [InlineData(126, "before the build")]
    [InlineData(127, "before the build")]
    public async Task APreCheckThatDidNotRunGivesItsCodeAndItsStandardError(int code, string when)
    {
        await using GateSandbox box = Answering(code, "", "sh: grep: not found");
        using var progress = new StringWriter();
        string? text = await CheckGate.PreCheckAsync(await box.StartAsync(Ct), Command, new PreCheckWords(when, "refactor"), progress, cancellationToken: Ct);
        Assert.Equal($"The check command exited with the code {code} {when}, so it did not run. Its standard error:\n\nsh: grep: not found", text);
        Assert.Equal(["check: start", "check: red"], progress.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
