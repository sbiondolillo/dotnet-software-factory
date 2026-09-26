namespace Core;

/// <summary>Runs git for the factory: the clone, the branch, the commit, the push, and the read of the result.</summary>
public class Git
{
    private const string CredentialHelper =
        "!sh -c 'cat >/dev/null; echo username=x-access-token; echo password=$FACTORY_GITHUB_TOKEN'";

    private readonly CommandRunner _runner;
    private string? _workingDirectory;

    /// <summary>Creates the git over the <paramref name="runner"/> that runs its commands.</summary>
    public Git(CommandRunner runner) => _runner = runner;

    /// <summary>Clones <paramref name="url"/> into <paramref name="directory"/>, authenticating with <paramref name="token"/>.</summary>
    public async Task CloneAsync(string url, string directory, string token)
    {
        _workingDirectory = directory;
        await RunAsync("clone", new CommandSpec
        {
            Command = "git",
            Arguments = ["clone", url, directory],
            EnvironmentVariables = CredentialEnvironment(token),
        });
    }

    /// <summary>Checks out a new branch <paramref name="branch"/> in the clone.</summary>
    public Task CheckoutBranchAsync(string branch)
        => RunAsync("checkout", new CommandSpec
        {
            Command = "git",
            Arguments = ["checkout", "-b", branch],
            WorkingDirectory = _workingDirectory,
        });

    /// <summary>Stages every change and commits it, with the author carried in the commit's environment.</summary>
    public async Task CommitAsync(string message, string authorName, string authorEmail)
    {
        await RunAsync("add", new CommandSpec
        {
            Command = "git",
            Arguments = ["add", "-A"],
            WorkingDirectory = _workingDirectory,
        });

        await RunAsync("commit", new CommandSpec
        {
            Command = "git",
            Arguments = ["commit", "-m", message],
            WorkingDirectory = _workingDirectory,
            EnvironmentVariables =
            {
                ["GIT_AUTHOR_NAME"] = authorName,
                ["GIT_AUTHOR_EMAIL"] = authorEmail,
                ["GIT_COMMITTER_NAME"] = authorName,
                ["GIT_COMMITTER_EMAIL"] = authorEmail,
            },
        });
    }

    /// <summary>Pushes <paramref name="branch"/> to origin, authenticating with <paramref name="token"/>.</summary>
    public Task PushAsync(string branch, string token)
        => RunAsync("push", new CommandSpec
        {
            Command = "git",
            Arguments = ["push", "origin", branch],
            WorkingDirectory = _workingDirectory,
            EnvironmentVariables = CredentialEnvironment(token),
        });

    /// <summary>Reads the head SHA: the 40 characters git printed, with nothing after them.</summary>
    public async Task<string> ReadHeadShaAsync()
    {
        CommandResult result = await _runner.RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["rev-parse", "HEAD"],
            WorkingDirectory = _workingDirectory,
        });

        if (result.ExitCode != 0)
        {
            throw new GitException("rev-parse", result);
        }

        return result.StandardOutput.Trim();
    }

    /// <summary>Lists the files changed since <paramref name="baseCommit"/> and the head.</summary>
    public async Task<IReadOnlyList<string>> ListChangedFilesAsync(string baseCommit)
    {
        CommandResult result = await _runner.RunAsync(new CommandSpec
        {
            Command = "git",
            Arguments = ["diff", "--name-only", $"{baseCommit}..HEAD"],
            WorkingDirectory = _workingDirectory,
        });

        if (result.ExitCode != 0)
        {
            throw new GitException("diff", result);
        }

        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static Dictionary<string, string> CredentialEnvironment(string token) => new()
    {
        ["FACTORY_GITHUB_TOKEN"] = token,
        ["GIT_CONFIG_COUNT"] = "2",
        ["GIT_CONFIG_KEY_0"] = "credential.helper",
        ["GIT_CONFIG_VALUE_0"] = "",
        ["GIT_CONFIG_KEY_1"] = "credential.helper",
        ["GIT_CONFIG_VALUE_1"] = CredentialHelper,
    };

    private async Task RunAsync(string subcommand, CommandSpec spec)
    {
        CommandResult result = await _runner.RunAsync(spec);
        if (result.ExitCode != 0)
        {
            throw new GitException(subcommand, result);
        }
    }
}
