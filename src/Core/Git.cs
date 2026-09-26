namespace Core;

/// <summary>The one place that runs git, over a <see cref="CommandRunner"/>.</summary>
public sealed class Git(CommandRunner runner)
{
    /// <summary>Clones the repository into <paramref name="cloneDirectory"/>, authenticating with <paramref name="token"/>.</summary>
    public Task CloneAsync(string owner, string repo, string token, string cloneDirectory)
        => RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["clone", $"https://github.com/{owner}/{repo}.git", cloneDirectory],
            EnvironmentVariables = CredentialHelperEnvironment(token),
        }, "clone");

    /// <summary>Checks out a new branch in the clone.</summary>
    public Task CheckoutNewBranchAsync(string cloneDirectory, string branchName)
        => RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["checkout", "-b", branchName],
            WorkingDirectory = cloneDirectory,
        }, "checkout");

    /// <summary>Stages every change in the work tree and commits it, carrying <paramref name="authorName"/> and <paramref name="authorEmail"/> in the commit's environment.</summary>
    public async Task CommitAllAsync(string cloneDirectory, string message, string authorName, string authorEmail)
    {
        await RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["add", "-A"],
            WorkingDirectory = cloneDirectory,
        }, "add").ConfigureAwait(false);

        await RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["commit", "-m", message],
            WorkingDirectory = cloneDirectory,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = authorName,
                ["GIT_AUTHOR_EMAIL"] = authorEmail,
                ["GIT_COMMITTER_NAME"] = authorName,
                ["GIT_COMMITTER_EMAIL"] = authorEmail,
            },
        }, "commit").ConfigureAwait(false);
    }

    /// <summary>Pushes the branch to origin, authenticating with <paramref name="token"/>.</summary>
    public Task PushAsync(string cloneDirectory, string branchName, string token)
        => RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["push", "origin", branchName],
            WorkingDirectory = cloneDirectory,
            EnvironmentVariables = CredentialHelperEnvironment(token),
        }, "push");

    /// <summary>Reads the head SHA of the clone, the 40 characters git prints with nothing after them.</summary>
    public async Task<string> ReadHeadShaAsync(string cloneDirectory)
    {
        CommandResult result = await RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["rev-parse", "HEAD"],
            WorkingDirectory = cloneDirectory,
        }, "rev-parse").ConfigureAwait(false);

        return result.StandardOutput.Length > 40 ? result.StandardOutput[..40] : result.StandardOutput;
    }

    /// <summary>Lists the files changed since <paramref name="baseCommit"/>, in the order git names them.</summary>
    public async Task<IReadOnlyList<string>> ListChangedFilesAsync(string cloneDirectory, string baseCommit)
    {
        CommandResult result = await RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["diff", "--name-only", baseCommit],
            WorkingDirectory = cloneDirectory,
        }, "diff").ConfigureAwait(false);

        return result.StandardOutput.Split('\n').Where(file => file.Length > 0).ToList();
    }

    /// <summary>Clears any inherited credential helper and sets one that answers with <paramref name="token"/>.</summary>
    private static Dictionary<string, string> CredentialHelperEnvironment(string token) => new()
    {
        ["FACTORY_GITHUB_TOKEN"] = token,
        ["GIT_CONFIG_COUNT"] = "2",
        ["GIT_CONFIG_KEY_0"] = "credential.helper",
        ["GIT_CONFIG_VALUE_0"] = "",
        ["GIT_CONFIG_KEY_1"] = "credential.helper",
        ["GIT_CONFIG_VALUE_1"] = "!f() { echo username=git; echo password=$FACTORY_GITHUB_TOKEN; }; f",
    };

    private async Task<CommandResult> RunAsync(CommandSpec spec, string subcommand)
    {
        CommandResult result = await runner.RunAsync(spec).ConfigureAwait(false);
        if (result.ExitCode is not 0)
        {
            throw new GitCommandException(subcommand, result.StandardError);
        }

        return result;
    }
}
