using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitSteps
{
    private readonly List<CommandSpec> _specs = [];

    private Git? _git;
    private CommandSpec? _cloneSpec;
    private CommandSpec? _checkoutSpec;
    private CommandSpec? _commitSpec;
    private CommandSpec? _pushSpec;
    private string? _headSha;
    private IReadOnlyList<string>? _changedFiles;
    private Exception? _operationException;
    private Exception? _pushException;

    [Given("a Git built over a mock CommandRunner that returns exit code 0 for every command")]
    public void GivenGitOverASuccessfulMock() => MockGit(0, "", "");

    [Given("a Git built over a mock CommandRunner that returns exit code 0 and standard output {string} for every command")]
    public void GivenGitOverAMockThatPrints(string standardOutput) => MockGit(0, standardOutput, "");

    [Given("a Git built over a mock CommandRunner that returns exit code {int} and standard error {string} for every command")]
    public void GivenGitOverAFailingMock(int exitCode, string standardError) => MockGit(exitCode, "", standardError);

    [When("I clone {string} with the token {string} into the directory {string}")]
    public async Task WhenIClone(string url, string token, string directory)
    {
        await _git!.CloneAsync(url, token, directory);
        _cloneSpec = _specs[^1];
    }

    [When("I check out the branch {string} in {string}")]
    public async Task WhenICheckoutTheBranch(string branch, string directory)
    {
        await _git!.CheckoutBranchAsync(directory, branch);
        _checkoutSpec = _specs[^1];
    }

    [When("I commit all changes in {string} with the message {string}, the author name {string}, and the author email {string}")]
    public async Task WhenICommitAllChanges(string directory, string message, string authorName, string authorEmail)
    {
        await _git!.CommitAllAsync(directory, message, authorName, authorEmail);
        _commitSpec = _specs[^1];
    }

    [When("I push the branch {string} from {string} with the token {string}")]
    public async Task WhenIPushTheBranch(string branch, string directory, string token)
    {
        try
        {
            await _git!.PushAsync(directory, branch, token);
            _pushException = null;
        }
        catch (Exception exception)
        {
            _pushException = exception;
        }

        _pushSpec = _specs[^1];
    }

    [When("I read the head SHA of {string}")]
    public async Task WhenIReadTheHeadSha(string directory) => _headSha = await _git!.ReadHeadShaAsync(directory);

    [When("I list the files changed since {string} in {string}")]
    public async Task WhenIListTheFilesChangedSince(string baseCommit, string directory)
    {
        try
        {
            _changedFiles = await _git!.ListChangedFilesAsync(directory, baseCommit);
        }
        catch (Exception exception)
        {
            _changedFiles = null;
            _operationException = exception;
        }
    }

    [Then("every CommandSpec sent to the mock names the program {string}")]
    public void ThenEveryCommandSpecNamesTheProgram(string program)
        => Assert.All(_specs, spec => Assert.Equal(program, spec.Command));

    [Then("every CommandSpec sent to the mock but the clone's has the working directory {string}")]
    public void ThenEveryCommandSpecButTheClonesHasTheWorkingDirectory(string directory)
        => Assert.All(_specs.Where(spec => spec != _cloneSpec), spec => Assert.Equal(directory, spec.WorkingDirectory));

    [Then("the clone's CommandSpec has an argument equal to {string}")]
    public void ThenTheClonesCommandSpecHasAnArgumentEqualTo(string argument)
        => Assert.Contains(argument, _cloneSpec!.Arguments);

    [Then("the clone's CommandSpec has no argument equal to {string}")]
    public void ThenTheClonesCommandSpecHasNoArgumentEqualTo(string argument)
        => Assert.DoesNotContain(argument, _cloneSpec!.Arguments);

    [Then("the checkout's CommandSpec has the arguments {string}, {string} and {string}, in that order")]
    public void ThenTheCheckoutsCommandSpecHasTheArgumentsInThatOrder(string first, string second, string third)
        => Assert.Equal([first, second, third], _checkoutSpec!.Arguments);

    [Then("the {word}'s CommandSpec has the environment variable {string} set to {string}")]
    public void ThenTheCommandSpecHasTheEnvironmentVariableSetTo(string operation, string name, string value)
    {
        CommandSpec spec = SpecFor(operation);
        Assert.Contains(name, spec.EnvironmentVariables.Keys);
        Assert.Equal(value, spec.EnvironmentVariables[name]);
    }

    [Then("the {word}'s CommandSpec has these environment variables:")]
    public void ThenTheCommandSpecHasTheseEnvironmentVariables(string operation, Table table)
    {
        CommandSpec spec = SpecFor(operation);
        foreach (DataTableRow row in table.Rows)
        {
            string name = row["name"];
            Assert.Contains(name, spec.EnvironmentVariables.Keys);
            Assert.Equal(row["value"], spec.EnvironmentVariables[name]);
        }
    }

    [Then("the {word}'s CommandSpec has an environment variable {string} that contains {string}")]
    public void ThenTheCommandSpecHasAnEnvironmentVariableThatContains(string operation, string name, string expected)
    {
        CommandSpec spec = SpecFor(operation);
        Assert.Contains(name, spec.EnvironmentVariables.Keys);
        Assert.Contains(expected, spec.EnvironmentVariables[name]);
    }

    [Then("the {word}'s CommandSpec has an environment variable {string} that contains {string} and {string}")]
    public void ThenTheCommandSpecHasAnEnvironmentVariableThatContainsBoth(string operation, string name, string first, string second)
    {
        CommandSpec spec = SpecFor(operation);
        Assert.Contains(name, spec.EnvironmentVariables.Keys);
        Assert.Contains(first, spec.EnvironmentVariables[name]);
        Assert.Contains(second, spec.EnvironmentVariables[name]);
    }

    [Then("the head SHA is {string}")]
    public void ThenTheHeadShaIs(string expected) => Assert.Equal(expected, _headSha);

    [Then("the list of changed files holds {string}")]
    public void ThenTheListOfChangedFilesHolds(string file) => Assert.Contains(file, _changedFiles!);

    [Then("the operation throws an exception whose message holds {string}")]
    public void ThenTheOperationThrowsAnExceptionWhoseMessageHolds(string text)
    {
        Assert.NotNull(_operationException);
        Assert.Contains(text, _operationException.Message);
    }

    [Then("the push throws an exception whose message holds {string}")]
    public void ThenThePushThrowsAnExceptionWhoseMessageHolds(string text)
    {
        Assert.NotNull(_pushException);
        Assert.Contains(text, _pushException.Message);
    }

    private CommandSpec SpecFor(string operation)
    {
        CommandSpec? spec = operation switch
        {
            "clone" => _cloneSpec,
            "checkout" => _checkoutSpec,
            "commit" => _commitSpec,
            "push" => _pushSpec,
            _ => null,
        };
        Assert.NotNull(spec);
        return spec;
    }

    private void MockGit(int exitCode, string standardOutput, string standardError)
    {
        var mock = new Mock<CommandRunner>();
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(_specs.Add)
            .ReturnsAsync(new CommandResult { ExitCode = exitCode, StandardOutput = standardOutput, StandardError = standardError });
        _git = new Git(mock.Object);
    }
}
