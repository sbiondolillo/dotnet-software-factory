using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class LintGateSteps(ExceptionState exceptionState) : IDisposable
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string WorkTree = "/tmp/factory-lint-work-tree";
    private readonly List<CommandSpec> _recorded = [];
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-lintgate-steps-");
    private CommandResult _format = new();
    private CommandResult _status = new();
    private Git? _git;
    private LintGate? _gate;
    private Sandbox? _sandbox;
    private bool _committed;
    private bool _hasChanges;

    public void Dispose()
    {
        _sandbox?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _directory.Delete(recursive: true);
    }

    [Given("a LintGate built over a Sandbox and a Git over a mock CommandRunner")]
    public async Task GivenALintGate()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(spec.Command == "docker"
                    ? spec.Arguments[0] switch
                    {
                        "run" => new CommandResult { StandardOutput = ContainerId + "\n" },
                        "exec" => _format,
                        _ => new CommandResult(),
                    }
                    : spec.Arguments[0] == "status" ? _status : new CommandResult());
            });
        _sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _directory.FullName, WorkTree, TestContext.Current.CancellationToken);
        _git = new Git(mock.Object);
        _gate = new LintGate(_sandbox, _git);
        _recorded.Clear();
    }

    [Given("a Git built over a mock CommandRunner")]
    public void GivenAGit()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(spec.Arguments[0] == "status" ? _status : new CommandResult());
            });
        _git = new Git(mock.Object);
    }

    [Given("dotnet format exits with the code {int} and writes nothing to standard error")]
    public void GivenFormatSilent(int code) => _format = new CommandResult { ExitCode = code };

    [Given("dotnet format exits with the code {int} and writes to standard error {string}")]
    public void GivenFormatError(int code, string error) => _format = new CommandResult { ExitCode = code, StandardError = error };

    [Given("dotnet format exits with the code {int} and writes a line to standard error")]
    public void GivenFormatLine(int code) =>
        _format = new CommandResult { ExitCode = code, StandardError = "An issue was encountered verifying workloads" };

    [Given("git reports no change in the work tree")]
    public void GivenNoChange() => _status = new CommandResult();

    [Given("git reports one change, the new file {string}")]
    public void GivenOneChange(string file) => _status = new CommandResult { StandardOutput = $"?? {file}\n" };

    [When("a caller runs the lint gate on a work tree")]
    public Task WhenRuns() => RunGate("Format", "Ada Lovelace", "ada@example.com");

    [When("a caller runs the lint gate with the message {string}, the author name {string} and the author email {string}")]
    public Task WhenRunsWith(string message, string name, string email) => RunGate(message, name, email);

    [When("a caller asks Git whether the work tree holds a change")]
    public async Task WhenAsks() => _hasChanges = await _git!.HasChangesAsync(WorkTree, TestContext.Current.CancellationToken);

    [Then("the Sandbox ran dotnet format")]
    public void ThenRanFormat()
    {
        CommandSpec spec = Assert.Single(_recorded, s => s.Command == "docker");
        Assert.Equal(["exec", ContainerId, "dotnet", "format", "--no-restore"], spec.Arguments);
    }

    [Then("LintGate made one commit of every change")]
    public void ThenOneCommit()
    {
        Assert.Single(_recorded, s => Is(s, "commit"));
        Assert.Contains(_recorded, s => Is(s, "add") && s.Arguments.Contains("-A"));
    }

    [Then("the commit carries the message {string}, the author name {string} and the author email {string}")]
    public void ThenCommitCarries(string message, string name, string email)
    {
        CommandSpec spec = _recorded.Single(s => Is(s, "commit"));
        Assert.Contains(message, spec.Arguments);
        Assert.Equal(name, spec.EnvironmentVariables["GIT_AUTHOR_NAME"]);
        Assert.Equal(email, spec.EnvironmentVariables["GIT_AUTHOR_EMAIL"]);
        Assert.Equal(name, spec.EnvironmentVariables["GIT_COMMITTER_NAME"]);
        Assert.Equal(email, spec.EnvironmentVariables["GIT_COMMITTER_EMAIL"]);
    }

    [Then("the result says a commit was made")]
    public void ThenResultCommit() => Assert.True(_committed);

    [Then("the result says no commit was made")]
    public void ThenResultNoCommit() => Assert.False(_committed);

    [Then("LintGate made no commit")]
    public void ThenNoCommit() => Assert.DoesNotContain(_recorded, s => Is(s, "commit"));

    [Then("the commands ran in this order: dotnet format, the git change query, the git commit")]
    public void ThenOrder()
    {
        List<CommandSpec> important = [.. _recorded.Where(s => s.Command == "docker" || Is(s, "status") || Is(s, "commit"))];
        Assert.Equal(3, important.Count);
        Assert.Equal("docker", important[0].Command);
        Assert.Equal(["status", "--porcelain"], important[1].Arguments);
        Assert.Equal("commit", important[2].Arguments[0]);
    }

    [Then("the git commit was the last command")]
    public void ThenCommitLast() => Assert.Equal("commit", _recorded[^1].Arguments[0]);

    [Then("the lint gate throws LintGateException")]
    public void ThenThrows() => Assert.IsType<LintGateException>(exceptionState.Exception);

    [Then("the exception message holds {string} and {string}")]
    public void ThenMessageHolds(string first, string second)
    {
        Assert.Contains(first, exceptionState.Exception!.Message, StringComparison.Ordinal);
        Assert.Contains(second, exceptionState.Exception.Message, StringComparison.Ordinal);
    }

    [Then("no git command ran")]
    public void ThenNoGit() => Assert.DoesNotContain(_recorded, s => s.Command == "git");

    [Then("Git answers that the work tree holds a change")]
    public void ThenHolds() => Assert.True(_hasChanges);

    [Then("Git answers that the work tree holds no change")]
    public void ThenHoldsNo() => Assert.False(_hasChanges);

    private static bool Is(CommandSpec spec, string subcommand) => spec.Command == "git" && spec.Arguments[0] == subcommand;

    private async Task RunGate(string message, string name, string email)
    {
        exceptionState.Exception = null;
        try
        {
            _committed = await _gate!.RunAsync(WorkTree, message, name, email, TestContext.Current.CancellationToken);
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }
}
