using System.Linq.Expressions;
using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class CommandRunnerMockSteps
{
    private Mock<CommandRunner>? _mock;
    private CommandCaller? _caller;
    private CommandResult? _canned;
    private CommandResult? _received;

    [Given("the caller holds a Moq mock of CommandRunner")]
    public void GivenTheCallerHoldsAMoqMockOfCommandRunner()
    {
        _mock = new Mock<CommandRunner>();
        _caller = new CommandCaller(_mock.Object);
    }

    [Given("the mock's RunAsync returns a CommandResult with exit code {int}, standard output {string} and empty standard error")]
    public void GivenTheMockReturnsACannedStandardOutput(int exitCode, string standardOutput) =>
        SetCanned(new CommandResult(exitCode, standardOutput, ""));

    [Given("the mock's RunAsync returns a CommandResult with exit code {int}, standard error {string} and empty standard output")]
    public void GivenTheMockReturnsACannedStandardError(int exitCode, string standardError) =>
        SetCanned(new CommandResult(exitCode, "", standardError));

    [When("the caller asks the runner to run {word} with the argument {word}")]
    public async Task WhenTheCallerAsksTheRunnerToRun(string program, string argument) =>
        _received = await _caller!.RunAsync(program, new[] { argument }).ConfigureAwait(false);

    [Then("the caller receives exactly the canned CommandResult")]
    public void ThenTheCallerReceivesExactlyTheCannedCommandResult() => Assert.Equal(_canned, _received);

    [Then("no program is started")]
    public void ThenNoProgramIsStarted() =>
        _mock!.Verify(RunSetup(), Times.Once());

    private void SetCanned(CommandResult result)
    {
        _canned = result;
        _mock!.Setup(RunSetup()).ReturnsAsync(result);
    }

    private static Expression<Func<CommandRunner, Task<CommandResult>>> RunSetup() =>
        mock => mock.RunAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<TimeSpan>());
}
