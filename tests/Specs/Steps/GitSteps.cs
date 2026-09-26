using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitSteps
{
    private Mock<CommandRunner>? _runner;
    private List<CommandSpec>? _captured;
    private Git? _git;
    private string? _headSha;
    private string[]? _changedFiles;
    private Exception? _exception;

    [Given("a Git built over a mock CommandRunner")]
    public void GivenAGitBuiltOverAMockCommandRunner() => BuildGit();

    [Given("a Git built over a mock CommandRunner cloning into {string} with the token {string}")]
    public async Task GivenAGitBuiltOverAMockCommandRunnerCloningInto(string directory, string token)
    {
        BuildGit();
        await _git!.CloneAsync("https://github.com/sbiondolillo/factory-scratch.git", directory, token);
    }

    [Given("the CommandRunner answers the head read with exit code {int} and standard output {string}")]
    public void GivenTheCommandRunnerAnswersTheHeadRead(int exitCode, string standardOutput) =>
        _runner!
            .Setup(run => run.RunAsync(It.Is<CommandSpec>(spec => spec.Arguments.Contains("rev-parse"))))
            .ReturnsAsync(new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput });

    [Given("the CommandRunner answers the changed-files read with exit code {int} and standard output {string}")]
    public void GivenTheCommandRunnerAnswersTheChangedFilesReadWithOutput(int exitCode, string standardOutput) =>
        _runner!
            .Setup(run => run.RunAsync(It.Is<CommandSpec>(spec => spec.Arguments.Contains("diff"))))
            .ReturnsAsync(new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput });

    [Given("the CommandRunner answers the changed-files read with exit code {int} and standard error {string}")]
    public void GivenTheCommandRunnerAnswersTheChangedFilesReadWithStandardError(int exitCode, string standardError) =>
        _runner!
            .Setup(run => run.RunAsync(It.Is<CommandSpec>(spec => spec.Arguments.Contains("diff"))))
            .ReturnsAsync(new CommandResult { ExitCode = exitCode, StandardError = standardError });

    [Given("the CommandRunner answers the push with exit code {int} and standard error {string}")]
    public void GivenTheCommandRunnerAnswersThePush(int exitCode, string standardError) =>
        _runner!
            .Setup(run => run.RunAsync(It.Is<CommandSpec>(spec => spec.Arguments.Contains("push"))))
            .ReturnsAsync(new CommandResult { ExitCode = exitCode, StandardError = standardError });

    [When("the factory clones {string} into {string} with the token {string}")]
    public async Task WhenTheFactoryClones(string url, string directory, string token) =>
        await _git!.CloneAsync(url, directory, token);

    [When("the factory checks out the branch {string}")]
    public async Task WhenTheFactoryChecksOutTheBranch(string branch) => await _git!.CheckoutBranchAsync(branch);

    [When("the factory pushes the branch {string}")]
    public async Task WhenTheFactoryPushesTheBranch(string branch)
    {
        _exception = null;
        try
        {
            await _git!.PushAsync(branch);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [When("the factory commits every change with the message {string}, the author name {string} and the author email {string}")]
    public async Task WhenTheFactoryCommitsEveryChange(string message, string authorName, string authorEmail) =>
        await _git!.CommitAllAsync(message, authorName, authorEmail);

    [When("the factory reads the head SHA")]
    public async Task WhenTheFactoryReadsTheHeadSha() => _headSha = await _git!.ReadHeadShaAsync();

    [When("the factory lists the files changed since {string}")]
    public async Task WhenTheFactoryListsTheFilesChangedSince(string baseRevision)
    {
        _exception = null;
        try
        {
            _changedFiles = await _git!.ListChangedFilesAsync(baseRevision);
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    [Then("the CommandRunner received a CommandSpec whose program is {string}")]
    public void ThenTheProgramIs(string expected) => Assert.Equal(expected, LastSpec().Command);

    [Then("the CommandRunner received a CommandSpec whose working directory is {string}")]
    public void ThenTheWorkingDirectoryIs(string expected) => Assert.Equal(expected, LastSpec().WorkingDirectory);

    [Then("the CommandRunner received a CommandSpec whose arguments include {string}")]
    public void ThenTheArgumentsInclude(string expected) => Assert.Contains(expected, LastSpec().Arguments);

    [Then("the CommandRunner received a CommandSpec whose arguments do not include {string}")]
    public void ThenTheArgumentsDoNotInclude(string expected) => Assert.DoesNotContain(expected, LastSpec().Arguments);

    [Then("the CommandRunner received a CommandSpec whose arguments are {string}, {string} and {string} in that order")]
    public void ThenTheArgumentsAreInThatOrder(string first, string second, string third) =>
        Assert.Equal([first, second, third], LastSpec().Arguments);

    [Then("the CommandRunner received a CommandSpec whose environment variable {string} is {string}")]
    public void ThenTheEnvironmentVariableIs(string name, string expected) =>
        Assert.Equal(expected, LastSpec().EnvironmentVariables[name]);

    [Then("the CommandRunner received a CommandSpec whose environment variables include:")]
    public void ThenTheEnvironmentVariablesInclude(Reqnroll.DataTable table)
    {
        CommandSpec spec = LastSpec();
        foreach (Reqnroll.DataTableRow row in table.Rows)
        {
            string name = row[0];
            string value = row[1];
            Assert.True(spec.EnvironmentVariables.TryGetValue(name, out string? actual), $"no environment variable named {name}");
            Assert.Equal(value, actual);
        }
    }

    [Then("the CommandRunner received a CommandSpec whose environment variable {string} contains {string}")]
    public void ThenTheEnvironmentVariableContains(string name, string expected) =>
        Assert.Contains(expected, LastSpec().EnvironmentVariables[name]);

    [Then("the head SHA is {string}")]
    public void ThenTheHeadShaIs(string expected) => Assert.Equal(expected, _headSha);

    [Then("the list of changed files holds {string}")]
    public void ThenTheListOfChangedFilesHolds(string expected) => Assert.Contains(expected, _changedFiles ?? []);

    [Then("the operation throws an exception whose message contains {string}")]
    public void ThenTheOperationThrowsAnExceptionWhoseMessageContains(string expected) => AssertExceptionMessageContains(expected);

    [Then("the push throws an exception whose message contains {string}")]
    public void ThenThePushThrowsAnExceptionWhoseMessageContains(string expected) => AssertExceptionMessageContains(expected);

    private void BuildGit()
    {
        _captured = [];
        _runner = new Mock<CommandRunner>();
        _runner
            .Setup(run => run.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(spec => _captured!.Add(spec))
            .ReturnsAsync(new CommandResult());
        _git = new Git(_runner.Object);
    }

    private CommandSpec LastSpec() => _captured![^1];

    private void AssertExceptionMessageContains(string expected)
    {
        Assert.NotNull(_exception);
        Assert.Contains(expected, _exception!.Message);
    }
}
