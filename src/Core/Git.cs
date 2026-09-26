namespace Core;

/// <summary>Runs git over a <see cref="CommandRunner"/>, authenticating to GitHub with a token.</summary>
public sealed class Git(CommandRunner runner, string token, string? directory = null)
{
    // The credential helper script, a git "!"-command. It holds the variable
    // reference to $FACTORY_GITHUB_TOKEN as literal text, so git's own shell
    // expands it from the child process's environment at run time, never a
    // value baked in at build time.
    private const string CredentialHelper =
        "!f() { echo username=x-access-token; echo password=$FACTORY_GITHUB_TOKEN; }; f";

    private readonly CommandRunner _runner = runner;
    private readonly string _token = token;
    private string? _directory = directory;

    /// <summary>Clones the repo into <paramref name="workTree"/>, and runs every later command in it.</summary>
    public async Task CloneAsync(string repoSlug, string workTree)
    {
        await RunAsync(["clone", $"https://github.com/{repoSlug}.git", workTree], AuthEnvironment()).ConfigureAwait(false);
        _directory = workTree;
    }

    /// <summary>Checks out the branch, creating it when the work tree has none by that name.</summary>
    public Task CheckoutBranchAsync(string branch) => RunAsync(["checkout", branch]);

    /// <summary>Stages every change, new untracked files among them, and commits it with the message and author given.</summary>
    public async Task CommitAsync(string message, string authorName, string authorEmail)
    {
        await RunAsync(["add", "-A"]).ConfigureAwait(false);

        var environment = new Dictionary<string, string>
        {
            ["GIT_COMMITTER_NAME"] = authorName,
            ["GIT_COMMITTER_EMAIL"] = authorEmail,
        };

        await RunAsync(
            ["commit", "-m", message, $"--author={authorName} <{authorEmail}>"],
            environment).ConfigureAwait(false);
    }

    /// <summary>Pushes the branch to its remote.</summary>
    public Task PushAsync(string branch) => RunAsync(["push", "origin", branch], AuthEnvironment());

    /// <summary>Reads the head SHA: the 40 characters git printed, with nothing after them.</summary>
    public async Task<string> ReadHeadShaAsync()
    {
        CommandResult result = await RunAsync(["rev-parse", "HEAD"]).ConfigureAwait(false);
        return result.StandardOutput.Trim();
    }

    /// <summary>Lists the files changed since <paramref name="baseRef"/>.</summary>
    public async Task<IReadOnlyList<string>> ListChangedFilesSinceAsync(string baseRef)
    {
        CommandResult result = await RunAsync(["diff", "--name-only", baseRef, "HEAD"]).ConfigureAwait(false);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private async Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, Dictionary<string, string>? environment = null)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = arguments,
            WorkingDirectory = _directory,
            EnvironmentVariables = environment ?? new Dictionary<string, string>(),
        };

        CommandResult result = await _runner.RunAsync(spec).ConfigureAwait(false);
        if (result.ExitCode is not 0)
        {
            throw new GitCommandException(arguments[0], result.StandardError);
        }

        return result;
    }

    private Dictionary<string, string> AuthEnvironment() => new()
    {
        ["FACTORY_GITHUB_TOKEN"] = _token,
        ["GIT_CONFIG_COUNT"] = "2",
        ["GIT_CONFIG_KEY_0"] = "credential.helper",
        ["GIT_CONFIG_VALUE_0"] = "",
        ["GIT_CONFIG_KEY_1"] = "credential.helper",
        ["GIT_CONFIG_VALUE_1"] = CredentialHelper,
    };
}
