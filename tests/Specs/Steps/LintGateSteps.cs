using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class LintGateSteps : IDisposable
{
    private const string WorkTree = "/tmp/factory-lint-tree";
    private const string Id = "abc123";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-lintgate-");
    private readonly List<CommandSpec> _recorded = [];
    private CommandResult _format = new();
    private string _status = "";
    private Git? _git;
    private LintGate? _gate;
    private Exception? _exception;
    private bool _committed;
    private bool _hasChanges;

    public void Dispose() => _directory.Delete(recursive: true);

    private CommandRunner Runner()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                if (spec.Command is "git")
                {
                    return Task.FromResult(new CommandResult
                    {
                        StandardOutput = spec.Arguments[0] is "status" ? _status : "",
                    });
                }

                return Task.FromResult(spec.Arguments[0] switch
                {
                    "run" => new CommandResult { StandardOutput = Id + "\n" },
                    "exec" => _format,
                    _ => new CommandResult(),
                });
            });
        return mock.Object;
    }

    [Given("a LintGate built over a Sandbox and a Git over a mock CommandRunner")]
    public async Task GivenALintGate()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        CommandRunner runner = Runner();
        Sandbox sandbox = await Sandbox.StartAsync(
            runner, "factory-sandbox:test", _directory.FullName, WorkTree, TestContext.Current.CancellationToken);
        _git = new Git(runner);
        _gate = new LintGate(sandbox, _git);
        _recorded.Clear();
    }

    [Given("a Git built over a mock CommandRunner")]
    public void GivenAGit() => _git = new Git(Runner());

    [Given("dotnet format exits with the code 0 and writes nothing")]
    public void GivenFormatWritesNothing() => _format = new CommandResult();

    [Given("dotnet format exits with the code 0 and writes a line to standard error")]
    public void GivenFormatWarns() => _format = new CommandResult { StandardError = "warning: a line" };

    [Given("dotnet format exits with the code {int} and writes a reason to standard error")]
    public void GivenFormatFails(int code) =>
        _format = new CommandResult { ExitCode = code, StandardError = "Could not find a MSBuild project file" };

    [Given("git reports no change in the work tree")]
    public void GivenNoChange() => _status = "";

    [Given("git reports one change in the work tree")]
    public void GivenOneChange() => _status = " M Greeter.cs\n";

    [Given("git reports one new file {string} in the work tree")]
    public void GivenOneNewFile(string name) => _status = $"?? {name}\n";

    [When("a caller runs the lint gate on a work tree")]
    public Task WhenRunOnWorkTree() => RunAsync("Format", "Factory", "factory@example.com");

    [When("a caller runs the lint gate with the message {string}, the author name {string} and the author email {string}")]
    public Task WhenRunWith(string message, string name, string email) => RunAsync(message, name, email);

    [When("a caller asks Git whether the work tree holds a change")]
    public async Task WhenAskGit() =>
        _hasChanges = await _git!.HasChangesAsync(WorkTree, TestContext.Current.CancellationToken);

    private async Task RunAsync(string message, string name, string email)
    {
        try
        {
            _committed = await _gate!.RunAsync(WorkTree, message, name, email, TestContext.Current.CancellationToken);
        }
        catch (LintGateException exception)
        {
            _exception = exception;
        }
    }

    private CommandSpec Commit() => _recorded.Single(s => s.Command is "git" && s.Arguments[0] is "commit");

    [Then("a docker exec command runs dotnet format")]
    public void ThenExecRunsFormat() =>
        Assert.Contains(_recorded, s => s.Command is "docker" && s.Arguments[0] is "exec"
            && s.Arguments.Skip(2).SequenceEqual(["dotnet", "format", "--no-restore"]));

    [Then("the git commit carries every change of the work tree")]
    public void ThenCommitCarriesEveryChange()
    {
        CommandSpec add = _recorded.Single(s => s.Command is "git" && s.Arguments[0] is "add");
        Assert.Equal(["add", "-A"], add.Arguments);
        Assert.Equal(WorkTree, add.WorkingDirectory);
        Assert.Equal(WorkTree, Commit().WorkingDirectory);
    }

    [Then("the git commit carries the message {string}, the author name {string} and the author email {string}")]
    public void ThenCommitCarries(string message, string name, string email)
    {
        CommandSpec commit = Commit();
        Assert.Equal(["commit", "-m", message], commit.Arguments);
        Assert.Equal(name, commit.EnvironmentVariables["GIT_AUTHOR_NAME"]);
        Assert.Equal(email, commit.EnvironmentVariables["GIT_AUTHOR_EMAIL"]);
        Assert.Equal(name, commit.EnvironmentVariables["GIT_COMMITTER_NAME"]);
        Assert.Equal(email, commit.EnvironmentVariables["GIT_COMMITTER_EMAIL"]);
    }

    [Then("the result says a commit was made")]
    public void ThenCommitMade() => Assert.True(_committed);

    [Then("the result says no commit was made")]
    public void ThenNoCommitMade() => Assert.False(_committed);

    [Then("no git commit runs")]
    public void ThenNoCommit() =>
        Assert.DoesNotContain(_recorded, s => s.Command is "git" && s.Arguments[0] is "commit");

    [Then("the commands run in this order")]
    public void ThenOrder(Table table)
    {
        List<string> expected = [.. table.Rows.Select(row => row[0])];
        List<string> actual = [.. _recorded.Select(s => s.Command is "git"
            ? $"git {s.Arguments[0]}"
            : string.Join(' ', s.Arguments.Skip(2).Take(2)))];
        Assert.Equal(expected, actual);
    }

    [Then("the lint gate throws LintGateException")]
    public void ThenThrows() => Assert.IsType<LintGateException>(_exception);

    [Then("the exception message holds the exit code {int} and the reason")]
    public void ThenMessageHolds(int code)
    {
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), _exception!.Message);
        Assert.Contains("Could not find a MSBuild project file", _exception.Message);
    }

    [Then("no git command runs")]
    public void ThenNoGit() => Assert.DoesNotContain(_recorded, s => s.Command is "git");

    [Then("the lint gate does not throw")]
    public void ThenNoThrow() => Assert.Null(_exception);

    [Then("Git answers that the work tree holds a change")]
    public void ThenChange() => Assert.True(_hasChanges);

    [Then("Git answers that the work tree holds no change")]
    public void ThenNoChange() => Assert.False(_hasChanges);
}
