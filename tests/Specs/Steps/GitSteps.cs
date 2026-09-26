using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitSteps
{
    private readonly Mock<CommandRunner> _mock = new();
    private readonly List<CommandSpec> _commands = [];
    private readonly List<(string FirstArgument, CommandResult Result)> _answers = [];

    private CommandResult _defaultResult = new() { ExitCode = 0 };
    private string? _token;
    private Git? _git;
    private string? _workTree;
    private Exception? _exception;
    private string? _headSha;
    private IReadOnlyList<string>? _changedFiles;
    private (string Name, string Value)[] _entries = [];

    public GitSteps()
    {
        _mock
            .Setup(mock => mock.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(_commands.Add)
            .Returns((CommandSpec spec) => Task.FromResult(AnswerFor(spec)));
    }

    [Given("the runner answers every command with exit code {int}")]
    public void GivenTheRunnerAnswersEveryCommandWithExitCode(int exitCode)
        => _defaultResult = new CommandResult { ExitCode = exitCode };

    [Given("Git is configured with the token {string}")]
    public void GivenGitIsConfiguredWithTheToken(string token) => _token = token;

    [Given("the runner answers the head-read command with exit code {int} and standard output {string}")]
    public void GivenTheRunnerAnswersTheHeadReadCommandWithExitCodeAndStandardOutput(int exitCode, string standardOutput)
        => _answers.Add(("rev-parse", new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput }));

    [Given("the runner answers the changed-files command with exit code {int} and standard output {string}")]
    public void GivenTheRunnerAnswersTheChangedFilesCommandWithExitCodeAndStandardOutput(int exitCode, string standardOutput)
        => _answers.Add(("diff", new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput }));

    [Given("the runner answers the changed-files command with exit code {int} and standard error {string}")]
    public void GivenTheRunnerAnswersTheChangedFilesCommandWithExitCodeAndStandardError(int exitCode, string standardError)
        => _answers.Add(("diff", new CommandResult { ExitCode = exitCode, StandardError = standardError }));

    [Given("the runner answers the push command with exit code {int} and standard error {string}")]
    public void GivenTheRunnerAnswersThePushCommandWithExitCodeAndStandardError(int exitCode, string standardError)
        => _answers.Add(("push", new CommandResult { ExitCode = exitCode, StandardError = standardError }));

    [When("Git clones the repo {string} into a work tree")]
    public async Task WhenGitClonesTheRepo(string repoSlug) => await Clone(repoSlug);

    [When("Git clones the repo {string} with the token {string} into a work tree")]
    public async Task WhenGitClonesTheRepoWithTheToken(string repoSlug, string token)
    {
        _token = token;
        await Clone(repoSlug);
    }

    [When("Git checks out the branch {string}")]
    public async Task WhenGitChecksOutTheBranch(string branch) => await Run(() => CreateGit().CheckoutBranchAsync(branch));

    [When("Git commits every change with the message {string}, the author name {string} and the author email {string}")]
    public async Task WhenGitCommitsEveryChange(string message, string authorName, string authorEmail)
        => await Run(() => CreateGit().CommitAsync(message, authorName, authorEmail));

    [When("Git pushes the branch {string}")]
    public async Task WhenGitPushesTheBranch(string branch) => await Run(() => CreateGit().PushAsync(branch));

    [When("Git reads the head SHA")]
    public async Task WhenGitReadsTheHeadSha() => _headSha = await Run(() => CreateGit().ReadHeadShaAsync());

    [When("Git lists the files changed since the base {string}")]
    public async Task WhenGitListsTheFilesChangedSinceTheBase(string baseRef)
        => _changedFiles = await Run(() => CreateGit().ListChangedFilesSinceAsync(baseRef));

    [Then("every command the runner ran names the program {string}")]
    public void ThenEveryCommandTheRunnerRanNamesTheProgram(string program)
    {
        Assert.NotEmpty(_commands);
        Assert.All(_commands, command => Assert.Equal(program, command.Command));
    }

    [Then("every command after the clone has the working directory of the clone's work tree")]
    public void ThenEveryCommandAfterTheCloneHasTheWorkingDirectoryOfTheCloneWorkTree()
    {
        Assert.NotNull(_workTree);
        Assert.NotEmpty(_commands);
        Assert.All(_commands.Skip(1), command => Assert.Equal(_workTree, command.WorkingDirectory));
    }

    [Then("the clone command's arguments hold {string}")]
    public void ThenTheCloneCommandsArgumentsHold(string value)
        => Assert.Contains(value, CloneCommand().Arguments);

    [Then("the clone command's arguments hold none of the token {string}")]
    public void ThenTheCloneCommandsArgumentsHoldNoneOfTheToken(string token)
        => Assert.DoesNotContain(token, CloneCommand().Arguments);

    [Then("the clone command's environment variable {string} is {string}")]
    public void ThenTheCloneCommandEnvironmentVariableIs(string name, string value)
        => Assert.Equal(value, EnvironmentVariable(CloneCommand(), name));

    [Then("the clone command's environment variable {string} holds {string}")]
    public void ThenTheCloneCommandEnvironmentVariableHolds(string name, string value)
        => Assert.Contains(value, EnvironmentVariable(CloneCommand(), name));

    [Then("the push command's environment variable {string} is {string}")]
    public void ThenThePushCommandEnvironmentVariableIs(string name, string value)
        => Assert.Equal(value, EnvironmentVariable(PushCommand(), name));

    [Then("the push command's environment variable {string} holds {string}")]
    public void ThenThePushCommandEnvironmentVariableHolds(string name, string value)
        => Assert.Contains(value, EnvironmentVariable(PushCommand(), name));

    [Then("the commit command's environment variable {string} is {string}")]
    public void ThenTheCommitCommandEnvironmentVariableIs(string name, string value)
        => Assert.Equal(value, EnvironmentVariable(CommitCommand(), name));

    [Then("the clone command's environment variables hold the following")]
    public void ThenTheCloneCommandEnvironmentVariablesHoldTheFollowing(Table table)
    {
        _entries = table.Rows.Select(row => (row["name"], row["value"])).ToArray();
        AssertEnvironmentVariables(CloneCommand(), _entries);
    }

    [Then("the push command's environment variables hold the same GIT_CONFIG entries")]
    public void ThenThePushCommandEnvironmentVariablesHoldTheSameGitConfigEntries()
        => AssertEnvironmentVariables(PushCommand(), _entries);

    [Then("the head SHA is {string}")]
    public void ThenTheHeadShaIs(string expected) => Assert.Equal(expected, _headSha);

    [Then("the list of changed files holds {string}")]
    public void ThenTheListOfChangedFilesHolds(string file)
    {
        Assert.NotNull(_changedFiles);
        Assert.Contains(file, _changedFiles);
    }

    [Then("listing the changed files throws an exception whose message holds {string}")]
    public void ThenListingTheChangedFilesThrowsAnExceptionWhoseMessageHolds(string text)
        => AssertTheExceptionMessageHolds(text);

    [Then("pushing throws an exception whose message holds {string}")]
    public void ThenPushingThrowsAnExceptionWhoseMessageHolds(string text)
        => AssertTheExceptionMessageHolds(text);

    private async Task Clone(string repoSlug)
    {
        string workTree = CreateWorkTree();
        _workTree = workTree;
        await Run(() => CreateGit().CloneAsync(repoSlug, workTree));
    }

    private static string CreateWorkTree()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"git-steps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private Git CreateGit() => _git ??= new Git(_mock.Object, _token ?? "");

    private CommandResult AnswerFor(CommandSpec spec)
    {
        foreach ((string firstArgument, CommandResult result) in _answers)
        {
            if (spec.Arguments[0] == firstArgument)
            {
                return result;
            }
        }

        return _defaultResult;
    }

    private async Task Run(Func<Task> operation)
    {
        _exception = null;
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            _exception = ex;
        }
    }

    private async Task<string> Run(Func<Task<string>> operation)
    {
        _exception = null;
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _exception = ex;
            return "";
        }
    }

    private async Task<IReadOnlyList<string>> Run(Func<Task<IReadOnlyList<string>>> operation)
    {
        _exception = null;
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _exception = ex;
            return [];
        }
    }

    private void AssertTheExceptionMessageHolds(string text)
    {
        Assert.NotNull(_exception);
        Assert.Contains(text, _exception.Message);
    }

    private CommandSpec CloneCommand() => CommandWithSubcommand("clone");

    private CommandSpec PushCommand() => CommandWithSubcommand("push");

    private CommandSpec CommitCommand() => CommandWithSubcommand("commit");

    private CommandSpec CommandWithSubcommand(string subcommand)
    {
        Assert.Contains(_commands, command => command.Arguments[0] == subcommand);
        return _commands.First(command => command.Arguments[0] == subcommand);
    }

    private static string EnvironmentVariable(CommandSpec command, string name)
    {
        Assert.Contains(name, command.EnvironmentVariables.Keys);
        return command.EnvironmentVariables[name];
    }

    private static void AssertEnvironmentVariables(CommandSpec command, (string Name, string Value)[] entries)
    {
        foreach ((string name, string value) in entries)
        {
            Assert.Equal(value, EnvironmentVariable(command, name));
        }
    }
}
