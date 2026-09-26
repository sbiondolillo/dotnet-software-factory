using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public sealed class GitTests
{
    [Fact]
    public async Task ACloneThatExitsNonZeroThrowsNamingTheCloneAndHoldingTheError()
    {
        (CommandRunner runner, _) = MockedRunner(("clone", new CommandResult { ExitCode = 1, StandardError = "boom" }));
        var git = new Git(runner, "token");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CloneAsync("owner/repo", "work"));

        Assert.Contains("git clone failed", exception.Message);
        Assert.Contains("boom", exception.Message);
    }

    [Fact]
    public async Task ACheckoutThatExitsNonZeroThrowsNamingTheCheckoutAndHoldingTheError()
    {
        (CommandRunner runner, _) = MockedRunner(
            ("checkout", new CommandResult { ExitCode = 1, StandardError = "pathspec did not match any file(s) known to git" }));
        var git = new Git(runner, "token");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CheckoutBranchAsync("factory/run"));

        Assert.Contains("git checkout failed", exception.Message);
        Assert.Contains("pathspec did not match", exception.Message);
    }

    [Fact]
    public async Task AnAddThatExitsNonZeroThrowsNamingTheAddBeforeTheCommitEverRuns()
    {
        (CommandRunner runner, List<CommandSpec> specs) = MockedRunner(
            ("add", new CommandResult { ExitCode = 1, StandardError = "the add failed" }));
        var git = new Git(runner, "token");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAsync("Update README", "Ada Factory", "ada@example.com"));

        Assert.Contains("git add failed", exception.Message);
        Assert.DoesNotContain(specs, spec => spec.Arguments[0] == "commit");
    }

    [Fact]
    public async Task ACommitThatExitsNonZeroThrowsNamingTheCommitAndHoldingTheError()
    {
        (CommandRunner runner, _) = MockedRunner(
            ("commit", new CommandResult { ExitCode = 1, StandardError = "nothing to commit, working tree clean" }));
        var git = new Git(runner, "token");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.CommitAsync("Update README", "Ada Factory", "ada@example.com"));

        Assert.Contains("git commit failed", exception.Message);
        Assert.Contains("nothing to commit", exception.Message);
    }

    [Fact]
    public async Task AHeadReadThatExitsNonZeroThrowsNamingTheRevParseAndHoldingTheError()
    {
        (CommandRunner runner, _) = MockedRunner(
            ("rev-parse", new CommandResult { ExitCode = 128, StandardError = "fatal: not a git repository" }));
        var git = new Git(runner, "token");

        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => git.ReadHeadShaAsync());

        Assert.Contains("git rev-parse failed", exception.Message);
        Assert.Contains("fatal: not a git repository", exception.Message);
    }

    [Fact]
    public async Task EveryFailingCommandsMessageNamesItsOwnSubcommandNotJustTheError()
    {
        foreach ((Func<Git, Task> operation, string subcommand) in FailingOperations())
        {
            (CommandRunner runner, _) = MockedRunner(
                (subcommand, new CommandResult { ExitCode = 1, StandardError = "the standard error" }));
            var git = new Git(runner, "token");

            GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(() => operation(git));

            Assert.Contains($"git {subcommand} failed", exception.Message);
            Assert.Contains("the standard error", exception.Message);
        }
    }

    [Fact]
    public async Task ACommitCarriesTheAuthorFlagAndTheCommitterEnvironment()
    {
        (CommandRunner runner, List<CommandSpec> specs) = MockedRunner();
        var git = new Git(runner, "token");

        await git.CommitAsync("Update README", "Ada Factory", "ada@example.com");

        CommandSpec commit = specs.Single(spec => spec.Arguments[0] == "commit");
        Assert.Contains("--author=Ada Factory <ada@example.com>", commit.Arguments);
        Assert.Equal("Ada Factory", commit.EnvironmentVariables["GIT_COMMITTER_NAME"]);
        Assert.Equal("ada@example.com", commit.EnvironmentVariables["GIT_COMMITTER_EMAIL"]);
    }

    [Fact]
    public async Task ACommitStagesEveryChangeWithAddAllBeforeItCommits()
    {
        (CommandRunner runner, List<CommandSpec> specs) = MockedRunner();
        var git = new Git(runner, "token");

        await git.CommitAsync("Update README", "Ada Factory", "ada@example.com");

        Assert.Equal(2, specs.Count);
        Assert.Equal("add", specs[0].Arguments[0]);
        Assert.Equal("-A", specs[0].Arguments[1]);
        Assert.Equal(2, specs[0].Arguments.Count);
        Assert.Equal("commit", specs[1].Arguments[0]);
    }

    [Fact]
    public async Task AHeadShaWithTrailingNewlineReturnsExactlyTheFortyCharacters()
    {
        const string sha = "903c6f7ea009814ad6cff2d387717be8f34bab03";
        (CommandRunner runner, _) = MockedRunner(
            ("rev-parse", new CommandResult { ExitCode = 0, StandardOutput = $"{sha}\n" }));
        var git = new Git(runner, "token");

        string head = await git.ReadHeadShaAsync();

        Assert.Equal(sha, head);
        Assert.Equal(40, head.Length);
    }

    private static (CommandRunner Runner, List<CommandSpec> Specs) MockedRunner(
        params (string FirstArgument, CommandResult Result)[] answers)
    {
        var specs = new List<CommandSpec>();
        var mock = new Mock<CommandRunner>();
        mock
            .Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>()))
            .Callback<CommandSpec>(specs.Add)
            .Returns((CommandSpec spec) => Task.FromResult(AnswerFor(spec, answers)));

        return (mock.Object, specs);
    }

    private static CommandResult AnswerFor(CommandSpec spec, (string FirstArgument, CommandResult Result)[] answers)
    {
        foreach ((string firstArgument, CommandResult result) in answers)
        {
            if (spec.Arguments[0] == firstArgument)
            {
                return result;
            }
        }

        return new CommandResult { ExitCode = 0 };
    }


    private static IEnumerable<(Func<Git, Task> Operation, string Subcommand)> FailingOperations()
    {
        yield return ((git => git.CloneAsync("owner/repo", "work")), "clone");
        yield return ((git => git.CheckoutBranchAsync("branch")), "checkout");
        yield return ((git => git.CommitAsync("message", "Ada", "ada@example.com")), "commit");
        yield return ((git => git.PushAsync("branch")), "push");
        yield return ((git => git.ReadHeadShaAsync()), "rev-parse");
        yield return ((git => git.ListChangedFilesSinceAsync("main")), "diff");
    }
}
