using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitSteps(ExceptionState exceptionState)
{
    private readonly List<CommandSpec> _recorded = [];
    private readonly List<CancellationToken> _recordedTokens = [];
    private readonly Dictionary<string, CommandResult> _results = new();
    private readonly Dictionary<string, CancellationTokenSource> _namedTokenSources = new();
    private readonly HashSet<string> _throwingOperations = new();

    private Mock<CommandRunner>? _mock;
    private Git? _git;
    private string _headSha = "";
    private string[] _changedFiles = [];

    [Given("a caller has a Git built over a mock of CommandRunner that records every CommandSpec it runs")]
    public void GivenACallerHasAGitBuiltOverAMockOfCommandRunner()
    {
        _mock = new Mock<CommandRunner>();
        _mock.Setup(mock => mock.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken token) =>
            {
                _recorded.Add(spec);
                _recordedTokens.Add(token);
                string operation = spec.Arguments.Count > 0 ? spec.Arguments[0] : "";
                if (_throwingOperations.Contains(operation))
                {
                    throw new OperationCanceledException();
                }

                CommandResult result = _results.TryGetValue(operation, out CommandResult? configured)
                    ? configured
                    : new CommandResult { ExitCode = 0 };
                return Task.FromResult(result);
            });
        _git = new Git(_mock.Object);
    }

    [Given("git answers the head read with exit code {int} and the standard output {string}")]
    public void GivenGitAnswersTheHeadReadWith(int exitCode, string standardOutput)
        => _results["rev-parse"] = new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput };

    [Given("git answers the changed-files read with exit code {int} and the standard output {string}")]
    public void GivenGitAnswersTheChangedFilesReadWith(int exitCode, string standardOutput)
        => _results["diff"] = new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput };

    [Given("git answers the checkout with exit code {int} and the standard error {string}")]
    public void GivenGitAnswersTheCheckoutWith(int exitCode, string standardError)
        => _results["checkout"] = new CommandResult { ExitCode = exitCode, StandardError = standardError };

    [Given("the mock CommandRunner throws OperationCanceledException for the {string} command")]
    public void GivenTheMockCommandRunnerThrowsOperationCanceledExceptionForTheCommand(string operation)
        => _throwingOperations.Add(operation);

    [When("Git clones {string} with the token {string} into {string}")]
    public Task WhenGitClones(string url, string token, string directory)
        => RunAsync(() => _git!.CloneAsync(url, token, directory));

    [When("Git clones {string} with the token {string} into {string} and the cancellation token {string}")]
    public Task WhenGitClones(string url, string token, string directory, string tokenName)
        => RunAsync(() => _git!.CloneAsync(url, token, directory, NamedToken(tokenName)));

    [When("Git checks out the new branch {string} in {string}")]
    public Task WhenGitChecksOutTheNewBranch(string branch, string directory)
        => RunAsync(() => _git!.CheckoutBranchAsync(branch, directory));

    [When("Git checks out the new branch {string} in {string} with the cancellation token {string}")]
    public Task WhenGitChecksOutTheNewBranch(string branch, string directory, string tokenName)
        => RunAsync(() => _git!.CheckoutBranchAsync(branch, directory, NamedToken(tokenName)));

    [When("Git commits all changes in {string} with the message {string} and the author name {string} and the author email {string}")]
    public Task WhenGitCommitsAllChanges(string directory, string message, string authorName, string authorEmail)
        => RunAsync(() => _git!.CommitAllAsync(directory, message, authorName, authorEmail));

    [When("Git commits all changes in {string} with the message {string} and the author name {string} and the author email {string} and the cancellation token {string}")]
    public Task WhenGitCommitsAllChanges(string directory, string message, string authorName, string authorEmail, string tokenName)
        => RunAsync(() => _git!.CommitAllAsync(directory, message, authorName, authorEmail, NamedToken(tokenName)));

    [When("Git pushes the branch {string} from {string} with the token {string}")]
    public Task WhenGitPushesTheBranch(string branch, string directory, string token)
        => RunAsync(() => _git!.PushAsync(branch, directory, token));

    [When("Git pushes the branch {string} from {string} with the token {string} and the cancellation token {string}")]
    public Task WhenGitPushesTheBranch(string branch, string directory, string token, string tokenName)
        => RunAsync(() => _git!.PushAsync(branch, directory, token, NamedToken(tokenName)));

    [When("Git reads the head SHA in {string}")]
    public async Task WhenGitReadsTheHeadSha(string directory)
    {
        try
        {
            _headSha = await _git!.ReadHeadShaAsync(directory);
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("Git reads the head SHA in {string} with the cancellation token {string}")]
    public async Task WhenGitReadsTheHeadSha(string directory, string tokenName)
    {
        try
        {
            _headSha = await _git!.ReadHeadShaAsync(directory, NamedToken(tokenName));
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("Git lists the files changed since {string} in {string}")]
    public async Task WhenGitListsTheFilesChangedSince(string baseCommit, string directory)
    {
        try
        {
            _changedFiles = await _git!.ListChangedFilesAsync(baseCommit, directory);
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("Git lists the files changed since {string} in {string} with the cancellation token {string}")]
    public async Task WhenGitListsTheFilesChangedSince(string baseCommit, string directory, string tokenName)
    {
        try
        {
            _changedFiles = await _git!.ListChangedFilesAsync(baseCommit, directory, NamedToken(tokenName));
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [Then("every CommandSpec the mock CommandRunner recorded has the program {string}")]
    public void ThenEveryCommandSpecRecordedHasTheProgram(string program)
    {
        Assert.NotEmpty(_recorded);
        Assert.All(_recorded, spec => Assert.Equal(program, spec.Command));
    }

    [Then("each of these CommandSpecs has the working directory {string}:")]
    public void ThenEachOfTheseCommandSpecsHasTheWorkingDirectory(string directory, Table table)
    {
        foreach (var row in table.Rows)
        {
            CommandSpec spec = FindSpec(MapOperation(row["operation"]));
            Assert.Equal(directory, spec.WorkingDirectory);
        }
    }

    [Then("the clone's CommandSpec holds the argument {string}")]
    public void ThenTheCloneSCommandSpecHoldsTheArgument(string argument)
        => Assert.Contains(argument, FindSpec("clone").Arguments);

    [Then("none of the clone's CommandSpec arguments equal {string}")]
    public void ThenNoneOfTheCloneSCommandSpecArgumentsEqual(string value)
        => Assert.DoesNotContain(value, FindSpec("clone").Arguments);

    [Then("the checkout's CommandSpec holds the arguments {string}, {string} and {string} in that order")]
    public void ThenTheCheckoutSCommandSpecHoldsTheArgumentsInThatOrder(string first, string second, string third)
        => Assert.Equal([first, second, third], FindSpec("checkout").Arguments);

    [Then("the clone's CommandSpec and the push's CommandSpec each hold the environment variable {string} set to {string}")]
    public void ThenTheCloneAndPushCommandSpecsEachHoldTheEnvironmentVariableSetTo(string name, string value)
    {
        Assert.Equal(value, FindSpec("clone").EnvironmentVariables[name]);
        Assert.Equal(value, FindSpec("push").EnvironmentVariables[name]);
    }

    [Then("the clone's CommandSpec and the push's CommandSpec each hold these environment variables:")]
    public void ThenTheCloneAndPushCommandSpecsEachHoldTheseEnvironmentVariables(Table table)
    {
        foreach (var row in table.Rows)
        {
            string name = row["name"];
            string value = row["value"];
            Assert.Equal(value, FindSpec("clone").EnvironmentVariables[name]);
            Assert.Equal(value, FindSpec("push").EnvironmentVariables[name]);
        }
    }

    [Then("each GIT_CONFIG_VALUE_1 holds {string}")]
    public void ThenEachGitConfigValue1Holds(string text)
    {
        Assert.Contains(text, FindSpec("clone").EnvironmentVariables["GIT_CONFIG_VALUE_1"]);
        Assert.Contains(text, FindSpec("push").EnvironmentVariables["GIT_CONFIG_VALUE_1"]);
    }

    [Then("each GIT_CONFIG_VALUE_1 holds {string} and {string}")]
    public void ThenEachGitConfigValue1HoldsBoth(string first, string second)
    {
        foreach (string value in new[]
                 {
                     FindSpec("clone").EnvironmentVariables["GIT_CONFIG_VALUE_1"],
                     FindSpec("push").EnvironmentVariables["GIT_CONFIG_VALUE_1"],
                 })
        {
            Assert.Contains(first, value);
            Assert.Contains(second, value);
        }
    }

    [Then("the commit's CommandSpec holds the environment variable {string} set to {string} and the environment variable {string} set to {string}")]
    public void ThenTheCommitSCommandSpecHoldsTwoEnvironmentVariables(string firstName, string firstValue, string secondName, string secondValue)
    {
        CommandSpec spec = FindSpec("commit");
        Assert.Equal(firstValue, spec.EnvironmentVariables[firstName]);
        Assert.Equal(secondValue, spec.EnvironmentVariables[secondName]);
    }

    [Then("the head SHA is {string}")]
    public void ThenTheHeadShaIs(string expected) => Assert.Equal(expected, _headSha);

    [Then("the list of changed files holds {string}")]
    public void ThenTheListOfChangedFilesHolds(string file) => Assert.Contains(file, _changedFiles);

    [Then("Git throws a GitCommandException whose message contains {string}")]
    public void ThenGitThrowsAGitCommandExceptionWhoseMessageContains(string text)
    {
        Assert.IsType<GitCommandException>(exceptionState.Exception);
        Assert.Contains(text, exceptionState.Exception!.Message);
    }

    [Then("every call the mock CommandRunner recorded received the cancellation token {string}")]
    public void ThenEveryCallTheMockCommandRunnerRecordedReceivedTheCancellationToken(string tokenName)
    {
        CancellationToken expected = NamedToken(tokenName);
        Assert.NotEmpty(_recordedTokens);
        Assert.All(_recordedTokens, token => Assert.Equal(expected, token));
    }

    [Then("the commit throws an OperationCanceledException")]
    public void ThenTheCommitThrowsAnOperationCanceledException() => Assert.IsType<OperationCanceledException>(exceptionState.Exception);

    [Then("the mock CommandRunner recorded no {string} CommandSpec")]
    public void ThenTheMockCommandRunnerRecordedNoCommandSpec(string operation)
        => Assert.DoesNotContain(_recorded, spec => spec.Arguments.Count > 0 && spec.Arguments[0] == operation);

    private CancellationToken NamedToken(string tokenName)
    {
        if (!_namedTokenSources.TryGetValue(tokenName, out CancellationTokenSource? source))
        {
            source = new CancellationTokenSource();
            _namedTokenSources[tokenName] = source;
        }

        return source.Token;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    private CommandSpec FindSpec(string operation)
        => _recorded.Last(spec => spec.Arguments.Count > 0 && spec.Arguments[0] == operation);

    private static string MapOperation(string operation) => operation switch
    {
        "head read" => "rev-parse",
        "changed files read" => "diff",
        _ => operation,
    };
}
