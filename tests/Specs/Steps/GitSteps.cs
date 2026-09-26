using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitSteps
{
    private readonly List<CommandSpec> _specs;
    private readonly Git _git;

    private string _cloneDirectory = "";
    private string _branchName = "";
    private string _owner = "";
    private string _repo = "";
    private string _token = "";
    private string _commitMessage = "";
    private string _authorName = "";
    private string _authorEmail = "";
    private string _baseCommit = "";
    private string _headSha = "";
    private IReadOnlyList<string> _changedFiles = [];
    private CommandResult _answer = new();
    private CommandSpec? _spec;
    private Exception? _exception;

    public GitSteps()
    {
        _specs = [];
        var mock = new Mock<CommandRunner>();
        mock
            .Setup(run => run.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(spec => _specs.Add(spec))
            .ReturnsAsync(() => _answer);
        _git = new Git(mock.Object);
    }

    [Given("the clone directory is {string}")]
    public void GivenTheCloneDirectoryIs(string cloneDirectory) => _cloneDirectory = cloneDirectory;

    [Given("the branch name is {string}")]
    public void GivenTheBranchNameIs(string branchName) => _branchName = branchName;

    [Given("the owner is {string}")]
    public void GivenTheOwnerIs(string owner) => _owner = owner;

    [Given("the repo is {string}")]
    public void GivenTheRepoIs(string repo) => _repo = repo;

    [Given("the token is {string}")]
    public void GivenTheTokenIs(string token) => _token = token;

    [Given("the commit message is {string}")]
    public void GivenTheCommitMessageIs(string commitMessage) => _commitMessage = commitMessage;

    [Given("the author name is {string}")]
    public void GivenTheAuthorNameIs(string authorName) => _authorName = authorName;

    [Given("the author email is {string}")]
    public void GivenTheAuthorEmailIs(string authorEmail) => _authorEmail = authorEmail;

    [Given("the base commit is {string}")]
    public void GivenTheBaseCommitIs(string baseCommit) => _baseCommit = baseCommit;

    [Given("the CommandRunner answers the next command with exit code {int} and standard output {string}")]
    public void GivenTheCommandRunnerAnswersWithStandardOutput(int exitCode, string standardOutput)
        => _answer = new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput };

    [Given("the CommandRunner answers the next command with exit code {int} and standard error {string}")]
    public void GivenTheCommandRunnerAnswersWithStandardError(int exitCode, string standardError)
        => _answer = new CommandResult { ExitCode = exitCode, StandardError = standardError };

    [When("Git checks out the new branch in that clone")]
    public Task WhenGitChecksOutTheNewBranchInThatClone()
        => Invoke(() => _git.CheckoutNewBranchAsync(_cloneDirectory, _branchName));

    [When("Git clones that owner and repo with that token into a new clone directory")]
    public Task WhenGitClonesThatOwnerAndRepoWithThatTokenIntoANewCloneDirectory()
    {
        string combined = $"{_owner}/{_repo}";
        int slash = combined.IndexOf('/');
        string owner = combined[..slash];
        string repo = combined[(slash + 1)..];
        return Invoke(() => _git.CloneAsync(owner, repo, _token, $"/tmp/factory-clone-{Guid.NewGuid():N}"));
    }

    [When("Git clones {string} with that token into that clone directory")]
    public Task WhenGitClonesWithThatTokenIntoThatCloneDirectory(string ownerAndRepo)
    {
        int slash = ownerAndRepo.IndexOf('/');
        return Invoke(() => _git.CloneAsync(ownerAndRepo[..slash], ownerAndRepo[(slash + 1)..], _token, _cloneDirectory));
    }

    [When("Git pushes that branch from that clone with that token")]
    public Task WhenGitPushesThatBranchFromThatCloneWithThatToken()
        => Invoke(() => _git.PushAsync(_cloneDirectory, _branchName, _token));

    [When("Git commits every change in that clone with that message, author name and author email")]
    public Task WhenGitCommitsEveryChangeInThatClone()
        => Invoke(() => _git.CommitAllAsync(_cloneDirectory, _commitMessage, _authorName, _authorEmail));

    [When("Git reads the head SHA of that clone")]
    public Task WhenGitReadsTheHeadShaOfThatClone()
        => Invoke(async () => _headSha = await _git.ReadHeadShaAsync(_cloneDirectory));

    [When("Git lists the files changed since that base commit in that clone")]
    public Task WhenGitListsTheFilesChangedSinceThatBaseCommitInThatClone()
        => Invoke(async () => _changedFiles = await _git.ListChangedFilesAsync(_cloneDirectory, _baseCommit));

    [Then("the CommandRunner receives a CommandSpec whose program is {string}")]
    public void ThenTheCommandRunnerReceivesACommandSpecWhoseProgramIs(string program)
        => _spec = Assert.Single(_specs, spec => spec.Command == program);

    [Then("that CommandSpec's working directory is {string}")]
    public void ThenThatCommandSpecsWorkingDirectoryIs(string workingDirectory)
    {
        Assert.NotNull(_spec);
        Assert.Equal(workingDirectory, _spec.WorkingDirectory);
    }

    [Then("the clone's CommandSpec's arguments include {string}")]
    public void ThenTheClonesCommandSpecsArgumentsInclude(string argument)
        => Assert.Contains(argument, SpecWithSubcommand("clone").Arguments);

    [Then("the clone's CommandSpec's arguments do not include {string}")]
    public void ThenTheClonesCommandSpecsArgumentsDoNotInclude(string argument)
        => Assert.DoesNotContain(argument, SpecWithSubcommand("clone").Arguments);

    [Then("the checkout's CommandSpec's arguments are {string}, {string} and {string} in that order")]
    public void ThenTheCheckoutsCommandSpecsArgumentsAreInThatOrder(string first, string second, string third)
        => Assert.Equal(new[] { first, second, third }, SpecWithSubcommand("checkout").Arguments);

    [Then("the clone's CommandSpec holds the environment variable {string} set to {string}")]
    public void ThenTheClonesCommandSpecHoldsTheEnvironmentVariable(string name, string value)
        => Assert.Equal(value, SpecWithSubcommand("clone").EnvironmentVariables.GetValueOrDefault(name));

    [Then("the push's CommandSpec holds the environment variable {string} set to {string}")]
    public void ThenThePushsCommandSpecHoldsTheEnvironmentVariable(string name, string value)
        => Assert.Equal(value, SpecWithSubcommand("push").EnvironmentVariables.GetValueOrDefault(name));

    [Then("the commit's CommandSpec holds the environment variable {string} set to {string}")]
    public void ThenTheCommitsCommandSpecHoldsTheEnvironmentVariable(string name, string value)
        => Assert.Equal(value, SpecWithSubcommand("commit").EnvironmentVariables.GetValueOrDefault(name));

    [Then("the clone's CommandSpec holds these environment variables:")]
    public void ThenTheClonesCommandSpecHoldsTheseEnvironmentVariables(Table table)
        => AssertHolds(SpecWithSubcommand("clone").EnvironmentVariables, table);

    [Then("the push's CommandSpec holds these environment variables:")]
    public void ThenThePushsCommandSpecHoldsTheseEnvironmentVariables(Table table)
        => AssertHolds(SpecWithSubcommand("push").EnvironmentVariables, table);

    [Then("the clone's CommandSpec's environment variable {string} holds {string}")]
    public void ThenTheClonesCommandSpecsEnvironmentVariableHolds(string name, string text)
        => Assert.Contains(text, SpecWithSubcommand("clone").EnvironmentVariables.GetValueOrDefault(name));

    [Then("the push's CommandSpec's environment variable {string} holds {string}")]
    public void ThenThePushsCommandSpecsEnvironmentVariableHolds(string name, string text)
        => Assert.Contains(text, SpecWithSubcommand("push").EnvironmentVariables.GetValueOrDefault(name));

    [Then("the head SHA is {string}")]
    public void ThenTheHeadShaIs(string expected) => Assert.Equal(expected, _headSha);

    [Then("the list of changed files holds {string}")]
    public void ThenTheListOfChangedFilesHolds(string file) => Assert.Contains(file, _changedFiles);

    [Then("Git throws a GitCommandException whose message names the subcommand {string} and holds the standard error {string}")]
    public void ThenGitThrowsAGitCommandExceptionNamingTheSubcommandAndHoldingTheStandardError(string subcommand, string standardError)
    {
        GitCommandException exception = Assert.IsType<GitCommandException>(_exception);
        Assert.Equal(subcommand, exception.Subcommand);
        Assert.Contains(subcommand, exception.Message);
        Assert.Contains(standardError, exception.Message);
    }

    private CommandSpec SpecWithSubcommand(string subcommand)
        => Assert.Single(_specs, spec => spec.Arguments[0] == subcommand);

    private static void AssertHolds(Dictionary<string, string> environment, Table table)
    {
        foreach (DataTableRow row in table.Rows)
        {
            Assert.Equal(row[1], environment.GetValueOrDefault(row[0]));
        }
    }

    private async Task Invoke(Func<Task> action)
    {
        _exception = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }
}
