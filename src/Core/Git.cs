namespace Core;

/// <summary>Runs git for the factory's clone-and-deliver workflow.</summary>
public sealed class Git(CommandRunner runner)
{
    private string _directory = "";
    private string _token = "";

    /// <summary>Clones <paramref name="url"/> into <paramref name="directory"/>, authenticating with <paramref name="token"/>.</summary>
    public async Task CloneAsync(string url, string directory, string token)
    {
        _directory = directory;
        _token = token;
        await RunAsync("clone", new CommandSpec
        {
            Command = "git",
            Arguments = ["clone", url, directory],
            EnvironmentVariables = CredentialEnvironment(token),
        });
    }

    /// <summary>Checks out a new branch named <paramref name="branch"/> in the clone.</summary>
    public async Task CheckoutBranchAsync(string branch)
    {
        await RunAsync("checkout", new CommandSpec
        {
            Command = "git",
            Arguments = ["checkout", "-b", branch],
            WorkingDirectory = _directory,
        });
    }

    /// <summary>Stages every change in the work tree and commits it with <paramref name="message"/> as <paramref name="authorName"/> &lt;<paramref name="authorEmail"/>&gt;.</summary>
    public async Task CommitAllAsync(string message, string authorName, string authorEmail)
    {
        await RunAsync("add", new CommandSpec
        {
            Command = "git",
            Arguments = ["add", "-A"],
            WorkingDirectory = _directory,
        });

        await RunAsync("commit", new CommandSpec
        {
            Command = "git",
            Arguments = ["commit", "-m", message],
            WorkingDirectory = _directory,
            EnvironmentVariables = new()
            {
                ["GIT_AUTHOR_NAME"] = authorName,
                ["GIT_AUTHOR_EMAIL"] = authorEmail,
                ["GIT_COMMITTER_NAME"] = authorName,
                ["GIT_COMMITTER_EMAIL"] = authorEmail,
            },
        });
    }

    /// <summary>Pushes <paramref name="branch"/> to the remote, authenticating with the clone's token.</summary>
    public async Task PushAsync(string branch)
    {
        await RunAsync("push", new CommandSpec
        {
            Command = "git",
            Arguments = ["push", "origin", branch],
            WorkingDirectory = _directory,
            EnvironmentVariables = CredentialEnvironment(_token),
        });
    }

    /// <summary>Returns the 40 characters git printed for the clone's head.</summary>
    public async Task<string> ReadHeadShaAsync()
    {
        CommandResult result = await RunAsync("rev-parse", new CommandSpec
        {
            Command = "git",
            Arguments = ["rev-parse", "HEAD"],
            WorkingDirectory = _directory,
        });
        return result.StandardOutput.Trim();
    }

    /// <summary>Returns the files changed between <paramref name="baseRevision"/> and the clone's head.</summary>
    public async Task<string[]> ListChangedFilesAsync(string baseRevision)
    {
        CommandResult result = await RunAsync("diff", new CommandSpec
        {
            Command = "git",
            Arguments = ["diff", "--name-only", baseRevision, "HEAD"],
            WorkingDirectory = _directory,
        });
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Runs <paramref name="spec"/>, throwing <see cref="GitCommandException"/> when git exits with a code other than 0.</summary>
    private async Task<CommandResult> RunAsync(string subcommand, CommandSpec spec)
    {
        CommandResult result = await runner.RunAsync(spec).ConfigureAwait(false);
        if (result.ExitCode is not 0)
        {
            throw new GitCommandException(subcommand, result.StandardError);
        }

        return result;
    }

    /// <summary>The environment that authenticates git without touching the machine's credential store.</summary>
    private static Dictionary<string, string> CredentialEnvironment(string token) => new()
    {
        ["FACTORY_GITHUB_TOKEN"] = token,
        ["GIT_CONFIG_COUNT"] = "2",
        ["GIT_CONFIG_KEY_0"] = "credential.helper",
        ["GIT_CONFIG_VALUE_0"] = "",
        ["GIT_CONFIG_KEY_1"] = "credential.helper",
        ["GIT_CONFIG_VALUE_1"] = "!f() { echo password=$FACTORY_GITHUB_TOKEN; }; f",
    };
}
