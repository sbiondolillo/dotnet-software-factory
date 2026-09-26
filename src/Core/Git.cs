namespace Core;

/// <summary>Runs the git operations the app needs, over a <see cref="CommandRunner"/>.</summary>
public sealed class Git(CommandRunner runner)
{
    /// <summary>Clones <paramref name="url"/> into <paramref name="directory"/>, authenticating with <paramref name="token"/>.</summary>
    public async Task CloneAsync(string url, string token, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["clone", url, directory],
            EnvironmentVariables = CredentialEnvironment(token),
        };

        await RunAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Checks out a new branch named <paramref name="branch"/> in <paramref name="directory"/>.</summary>
    public async Task CheckoutBranchAsync(string branch, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["checkout", "-b", branch],
            WorkingDirectory = directory,
        };

        await RunAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stages and commits every change in the work tree at <paramref name="directory"/>.</summary>
    public async Task CommitAllAsync(string directory, string message, string authorName, string authorEmail, CancellationToken cancellationToken = default)
    {
        var addSpec = new CommandSpec
        {
            Command = "git",
            Arguments = ["add", "-A"],
            WorkingDirectory = directory,
        };

        await RunAsync(addSpec, cancellationToken).ConfigureAwait(false);

        var commitSpec = new CommandSpec
        {
            Command = "git",
            Arguments = ["commit", "-m", message],
            WorkingDirectory = directory,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = authorName,
                ["GIT_AUTHOR_EMAIL"] = authorEmail,
                ["GIT_COMMITTER_NAME"] = authorName,
                ["GIT_COMMITTER_EMAIL"] = authorEmail,
            },
        };

        await RunAsync(commitSpec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pushes <paramref name="branch"/> to origin from <paramref name="directory"/>, authenticating with <paramref name="token"/>.</summary>
    public async Task PushAsync(string branch, string directory, string token, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["push", "origin", branch],
            WorkingDirectory = directory,
            EnvironmentVariables = CredentialEnvironment(token),
        };

        await RunAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the 40-character SHA of HEAD in <paramref name="directory"/>.</summary>
    public async Task<string> ReadHeadShaAsync(string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["rev-parse", "HEAD"],
            WorkingDirectory = directory,
        };

        CommandResult result = await RunAsync(spec, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.Trim();
    }

    /// <summary>Lists the files changed since <paramref name="baseCommit"/> in <paramref name="directory"/>.</summary>
    public async Task<string[]> ListChangedFilesAsync(string baseCommit, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["diff", "--name-only", baseCommit],
            WorkingDirectory = directory,
        };

        CommandResult result = await RunAsync(spec, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken cancellationToken)
    {
        CommandResult result = await runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode is not 0)
        {
            throw new GitCommandException(spec.Arguments[0], result.StandardError);
        }

        return result;
    }

    private static Dictionary<string, string> CredentialEnvironment(string token) => new()
    {
        ["FACTORY_GITHUB_TOKEN"] = token,
        ["GIT_CONFIG_COUNT"] = "2",
        ["GIT_CONFIG_KEY_0"] = "credential.helper",
        ["GIT_CONFIG_VALUE_0"] = "",
        ["GIT_CONFIG_KEY_1"] = "credential.helper",
        ["GIT_CONFIG_VALUE_1"] = "!f() { echo username=x-access-token; echo \"password=$FACTORY_GITHUB_TOKEN\"; }; f",
    };
}
