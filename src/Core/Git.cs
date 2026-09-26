namespace Core;

/// <summary>Runs git for the library: the one place that builds a git <see cref="CommandSpec"/>.</summary>
public sealed class Git(CommandRunner runner)
{
    /// <summary>Clones the repository at <paramref name="url"/> into <paramref name="directory"/>, authenticating with <paramref name="token"/>.</summary>
    public Task CloneAsync(string url, string token, string directory)
        => RunGit("clone", ["clone", url, directory], null, TokenEnvironment(token));

    /// <summary>Checks out a new branch named <paramref name="branch"/> in the clone at <paramref name="directory"/>.</summary>
    public Task CheckoutBranchAsync(string directory, string branch)
        => RunGit("checkout", ["checkout", "-b", branch], directory);

    /// <summary>Stages every change in the work tree and commits it as the given author.</summary>
    public async Task CommitAllAsync(string directory, string message, string authorName, string authorEmail)
    {
        await RunGit("add", ["add", "-A"], directory).ConfigureAwait(false);
        await RunGit("commit", ["commit", "-m", message], directory, CommitterEnvironment(authorName, authorEmail)).ConfigureAwait(false);
    }

    /// <summary>Pushes <paramref name="branch"/> to its remote, authenticating with <paramref name="token"/>.</summary>
    public Task PushAsync(string directory, string branch, string token)
        => RunGit("push", ["push", "origin", branch], directory, TokenEnvironment(token));

    /// <summary>Reads the full SHA of the clone's HEAD.</summary>
    public async Task<string> ReadHeadShaAsync(string directory)
    {
        CommandResult result = await RunGit("rev-parse", ["rev-parse", "HEAD"], directory).ConfigureAwait(false);
        return result.StandardOutput.Trim();
    }

    /// <summary>Lists the files changed since <paramref name="baseCommit"/>.</summary>
    public async Task<IReadOnlyList<string>> ListChangedFilesAsync(string directory, string baseCommit)
    {
        CommandResult result = await RunGit("diff", ["diff", "--name-only", baseCommit], directory).ConfigureAwait(false);
        return result.StandardOutput.Split('\n').Select(line => line.Trim()).Where(line => line.Length is not 0).ToArray();
    }

    private async Task<CommandResult> RunGit(string subcommand, IReadOnlyList<string> arguments, string? workingDirectory, Dictionary<string, string>? environment = null)
    {
        CommandResult result = await runner.RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = environment ?? [],
        }).ConfigureAwait(false);

        if (result.ExitCode is not 0)
        {
            throw new GitCommandException(subcommand, result.StandardError);
        }

        return result;
    }

    private static Dictionary<string, string> TokenEnvironment(string token) => new()
    {
        ["FACTORY_GITHUB_TOKEN"] = token,
        ["GIT_CONFIG_COUNT"] = "2",
        ["GIT_CONFIG_KEY_0"] = "credential.helper",
        ["GIT_CONFIG_VALUE_0"] = "",
        ["GIT_CONFIG_KEY_1"] = "credential.helper",
        ["GIT_CONFIG_VALUE_1"] = "!f() { echo username=x-access-token; echo password=$FACTORY_GITHUB_TOKEN; }; f",
    };

    private static Dictionary<string, string> CommitterEnvironment(string authorName, string authorEmail) => new()
    {
        ["GIT_COMMITTER_NAME"] = authorName,
        ["GIT_COMMITTER_EMAIL"] = authorEmail,
        ["GIT_AUTHOR_NAME"] = authorName,
        ["GIT_AUTHOR_EMAIL"] = authorEmail,
    };
}
