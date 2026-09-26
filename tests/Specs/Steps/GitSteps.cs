using Core;
using Moq;
using Reqnroll;
using Xunit;
using Xunit.Sdk;

namespace Specs.Steps;

[Binding]
public sealed class GitSteps
{
    private readonly List<CommandSpec> _recorded = [];
    private Mock<CommandRunner>? _mock;
    private Git? _git;
    private string? _headSha;
    private IReadOnlyList<string>? _changedFiles;
    private Exception? _exception;

    [Given("a mock of CommandRunner records every CommandSpec it receives")]
    public void GivenAMockOfCommandRunnerRecordsEveryCommandSpecItReceives()
    {
        _mock = new Mock<CommandRunner>();
        _mock
            .Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(spec => _recorded.Add(spec))
            .ReturnsAsync(new CommandResult { ExitCode = 0 });
    }

    [Given("Git is built over that mock")]
    public void GivenGitIsBuiltOverThatMock() => _git = new Git(_mock!.Object);

    [Given("the mock of CommandRunner answers the head-read command with exit code {int} and standard output {string}")]
    public void GivenTheHeadReadCommandAnswers(int exitCode, string standardOutput)
        => Answer("rev-parse", exitCode, standardOutput, "");

    [Given("the mock of CommandRunner answers the changed-files command with exit code {int} and standard output {string}")]
    public void GivenTheChangedFilesCommandAnswersWithStandardOutput(int exitCode, string standardOutput)
        => Answer("diff", exitCode, standardOutput, "");

    [Given("the mock of CommandRunner answers the changed-files command with exit code {int} and standard error {string}")]
    public void GivenTheChangedFilesCommandAnswersWithStandardError(int exitCode, string standardError)
        => Answer("diff", exitCode, "", standardError);

    [Given("the mock of CommandRunner answers the push command with exit code {int} and standard error {string}")]
    public void GivenThePushCommandAnswers(int exitCode, string standardError)
        => Answer("push", exitCode, "", standardError);

    [When("Git clones {string} into {string} with the token {string}")]
    public async Task WhenGitClones(string url, string directory, string token)
        => await Run(() => _git!.CloneAsync(url, directory, token));

    [When("Git checks out the branch {string}")]
    public async Task WhenGitChecksOutTheBranch(string branch)
        => await Run(() => _git!.CheckoutBranchAsync(branch));

    [When("Git commits every change with the message {string}, the author name {string} and the author email {string}")]
    public async Task WhenGitCommitsEveryChange(string message, string authorName, string authorEmail)
        => await Run(() => _git!.CommitAsync(message, authorName, authorEmail));

    [When("Git pushes the branch {string} with the token {string}")]
    public async Task WhenGitPushesTheBranch(string branch, string token)
        => await Run(() => _git!.PushAsync(branch, token));

    [When("Git reads the head SHA")]
    public async Task WhenGitReadsTheHeadSha()
    {
        try
        {
            _headSha = await _git!.ReadHeadShaAsync();
            _exception = null;
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [When("Git lists the files changed since the base commit {string}")]
    public async Task WhenGitListsTheFilesChangedSinceTheBaseCommit(string baseCommit)
    {
        try
        {
            _changedFiles = await _git!.ListChangedFilesAsync(baseCommit);
            _exception = null;
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [Then("every recorded CommandSpec has the program {string}")]
    public void ThenEveryRecordedCommandSpecHasTheProgram(string program)
    {
        Assert.NotEmpty(_recorded);
        foreach (CommandSpec spec in _recorded)
        {
            Assert.Equal(program, spec.Command);
        }
    }

    [Then("every recorded CommandSpec but the clone's has the working directory {string}")]
    public void ThenEveryRecordedCommandSpecButTheClonesHasTheWorkingDirectory(string directory)
    {
        Assert.NotEmpty(_recorded);
        foreach (CommandSpec spec in _recorded)
        {
            if (IsSubcommand(spec, "clone"))
            {
                continue;
            }

            Assert.Equal(directory, spec.WorkingDirectory);
        }
    }

    [Then("the clone's CommandSpec arguments hold {string}")]
    public void ThenTheClonesCommandSpecArgumentsHold(string value)
        => Assert.Contains(value, CloneSpec().Arguments);

    [Then("no argument of the clone's CommandSpec holds {string}")]
    public void ThenNoArgumentOfTheClonesCommandSpecHolds(string value)
    {
        foreach (string argument in CloneSpec().Arguments)
        {
            Assert.DoesNotContain(value, argument);
        }
    }

    [Then("the clone's CommandSpec environment variable {string} is {string}")]
    public void ThenTheClonesCommandSpecEnvironmentVariableIs(string name, string value)
        => Assert.Equal(value, CloneSpec().EnvironmentVariables[name]);

    [Then("the clone's CommandSpec environment variable {string} holds {string}")]
    public void ThenTheClonesCommandSpecEnvironmentVariableHolds(string name, string value)
        => Assert.Contains(value, CloneSpec().EnvironmentVariables[name]);

    [Then("the push's CommandSpec environment variable {string} is {string}")]
    public void ThenThePushesCommandSpecEnvironmentVariableIs(string name, string value)
        => Assert.Equal(value, PushSpec().EnvironmentVariables[name]);

    [Then("the push's CommandSpec environment variable {string} holds {string}")]
    public void ThenThePushesCommandSpecEnvironmentVariableHolds(string name, string value)
        => Assert.Contains(value, PushSpec().EnvironmentVariables[name]);

    [Then("the commit's CommandSpec environment variable {string} is {string}")]
    public void ThenTheCommitsCommandSpecEnvironmentVariableIs(string name, string value)
        => Assert.Equal(value, CommitSpec().EnvironmentVariables[name]);

    [Then("the head SHA is {string}")]
    public void ThenTheHeadShaIs(string expected) => Assert.Equal(expected, _headSha);

    [Then("the list of changed files holds {string}")]
    public void ThenTheListOfChangedFilesHolds(string file)
        => Assert.Contains(file, _changedFiles ?? []);

    [Then("the operation throws an exception whose message holds {string}")]
    public void ThenTheOperationThrowsAnExceptionWhoseMessageHolds(string text)
    {
        Assert.NotNull(_exception);
        Assert.Contains(text, _exception.Message);
    }

    [Then("the push throws an exception whose message holds {string}")]
    public void ThenThePushThrowsAnExceptionWhoseMessageHolds(string text)
    {
        Assert.NotNull(_exception);
        Assert.Contains(text, _exception.Message);
    }

    private void Answer(string subcommand, int exitCode, string standardOutput, string standardError)
    {
        _mock!.Setup(runner => runner.RunAsync(
                It.Is<CommandSpec>(spec => IsSubcommand(spec, subcommand))))
            .Callback<CommandSpec>(spec => _recorded.Add(spec))
            .ReturnsAsync(new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput, StandardError = standardError });
    }

    private async Task Run(Func<Task> operation)
    {
        try
        {
            await operation();
            _exception = null;
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    private CommandSpec CloneSpec() => Find("clone");

    private CommandSpec PushSpec() => Find("push");

    private CommandSpec CommitSpec() => Find("commit");

    private CommandSpec Find(string subcommand) =>
        _recorded.FirstOrDefault(spec => IsSubcommand(spec, subcommand))
            ?? throw new XunitException($"no recorded CommandSpec runs {subcommand}");

    private static bool IsSubcommand(CommandSpec spec, string subcommand)
        => spec.Arguments.Count > 0 && spec.Arguments[0] == subcommand;
}
