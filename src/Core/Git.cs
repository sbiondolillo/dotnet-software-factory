using System.Globalization;

namespace Core;

/// <summary>Runs the git operations the app needs, over a <see cref="CommandRunner"/>.</summary>
public sealed class Git(CommandRunner runner)
{
    /// <summary>
    /// Clones <paramref name="url"/> into <paramref name="directory"/>, authenticating with <paramref name="token"/>,
    /// and appends <c>.factory/</c> to the clone's <c>.git/info/exclude</c>, so no commit holds a file the factory writes there.
    /// </summary>
    public async Task CloneAsync(string url, string token, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["clone", url, directory],
            EnvironmentVariables = CredentialEnvironment(token),
        };

        await RunAsync(spec, cancellationToken).ConfigureAwait(false);
        await ExcludeFactoryDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends the line <c>.factory/</c> to <c>.git/info/exclude</c> under <paramref name="directory"/>, which git reads as it reads
    /// <c>.gitignore</c> and never commits. A directory with no <c>.git</c> directory is left as it is.
    /// </summary>
    private static async Task ExcludeFactoryDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        string gitDirectory = Path.Combine(directory, ".git");
        if (!Directory.Exists(gitDirectory))
        {
            return;
        }

        string info = Directory.CreateDirectory(Path.Combine(gitDirectory, "info")).FullName;
        string exclude = Path.Combine(info, "exclude");
        string existing = File.Exists(exclude) ? await File.ReadAllTextAsync(exclude, cancellationToken).ConfigureAwait(false) : "";
        string separator = existing.Length is 0 || existing.EndsWith('\n') ? "" : "\n";
        await File.AppendAllTextAsync(exclude, $"{separator}.factory/\n", cancellationToken).ConfigureAwait(false);
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

    /// <summary>Checks out the branch <paramref name="branch"/> that origin holds, in <paramref name="directory"/>.</summary>
    public async Task CheckoutExistingBranchAsync(string branch, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["checkout", branch],
            WorkingDirectory = directory,
        };

        await RunAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Checks out the commit <paramref name="commit"/> in <paramref name="directory"/>, with the head detached at it.</summary>
    public async Task CheckoutCommitAsync(string commit, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["checkout", "--detach", commit],
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

    /// <summary>Gives the diff of the work tree at <paramref name="directory"/> against <paramref name="baseCommit"/>, as git prints it.</summary>
    public async Task<string> ReadDiffAsync(string baseCommit, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["diff", baseCommit],
            WorkingDirectory = directory,
        };

        CommandResult result = await RunAsync(spec, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput;
    }

    /// <summary>Reads the file <paramref name="path"/> as <paramref name="baseCommit"/> holds it, or null when git cannot show it.</summary>
    public async Task<string?> ReadFileAtAsync(string baseCommit, string path, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["show", $"{baseCommit}:{path}"],
            WorkingDirectory = directory,
        };

        try
        {
            return (await RunAsync(spec, cancellationToken).ConfigureAwait(false)).StandardOutput;
        }
        catch (GitCommandException)
        {
            return null;
        }
    }

    /// <summary>Tells whether the work tree at <paramref name="directory"/> holds a change, a new file included.</summary>
    public async Task<bool> HasChangesAsync(string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["status", "--porcelain"],
            WorkingDirectory = directory,
        };

        CommandResult result = await RunAsync(spec, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length is not 0;
    }

    private async Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken cancellationToken)
    {
        if (spec.WorkingDirectory is not null)
        {
            List<string> nested = NestedGitEntries(spec.WorkingDirectory);
            if (nested.Count is not 0)
            {
                throw new GitCommandException(
                    spec.Arguments[0], $"the work tree holds a .git other than its own, or a directory that cannot be read: {string.Join(", ", nested)}");
            }
        }

        TurnHooksOff(spec.EnvironmentVariables);
        CommandResult result = await runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode is not 0)
        {
            throw new GitCommandException(spec.Arguments[0], result.StandardError);
        }

        return result;
    }

    /// <summary>
    /// Adds <c>core.hooksPath=/dev/null</c> to <paramref name="environment"/> as the next <c>GIT_CONFIG_*</c> entry, after the
    /// entries it holds, so git runs no hook that a build or an agent wrote into the work tree.
    /// </summary>
    private static void TurnHooksOff(Dictionary<string, string> environment)
    {
        int count = environment.TryGetValue("GIT_CONFIG_COUNT", out string? text)
            ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;
        string index = count.ToString(CultureInfo.InvariantCulture);
        environment[$"GIT_CONFIG_KEY_{index}"] = "core.hooksPath";
        environment[$"GIT_CONFIG_VALUE_{index}"] = "/dev/null";
        environment["GIT_CONFIG_COUNT"] = (count + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Fast-forwards the current branch of <paramref name="directory"/> to <c>origin/<paramref name="branch"/></c>.</summary>
    public async Task PullAsync(string branch, string directory, string token, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["pull", "--ff-only", "origin", branch],
            WorkingDirectory = directory,
            EnvironmentVariables = CredentialEnvironment(token),
        };

        await RunAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes <paramref name="branch"/> on origin.</summary>
    public async Task DeleteRemoteBranchAsync(string branch, string directory, string token, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["push", "origin", "--delete", branch],
            WorkingDirectory = directory,
            EnvironmentVariables = CredentialEnvironment(token),
        };

        await RunAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the commit at the tip of <paramref name="branch"/> on origin, or null when origin lacks the branch.</summary>
    public async Task<string?> ReadRemoteHeadAsync(string branch, string directory, string token, CancellationToken cancellationToken = default)
    {
        // The full name of the ref, because a short pattern also matches the tail of a longer name, such as x/<branch>.
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["ls-remote", "--heads", "origin", $"refs/heads/{branch}"],
            WorkingDirectory = directory,
            EnvironmentVariables = CredentialEnvironment(token),
        };

        CommandResult result = await RunAsync(spec, cancellationToken).ConfigureAwait(false);
        string? line = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line?.Split('\t')[0];
    }

    /// <summary>Lists the tracked files under <paramref name="path"/> in <paramref name="directory"/>.</summary>
    public async Task<string[]> ListTrackedAsync(string path, string directory, CancellationToken cancellationToken = default)
    {
        var spec = new CommandSpec
        {
            Command = "git",
            Arguments = ["ls-files", "--", path],
            WorkingDirectory = directory,
        };

        CommandResult result = await RunAsync(spec, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Lists each directory, file or symlink named <c>.git</c> under <paramref name="workTree"/> other than its own,
    /// and each directory that cannot be read. Git runs the hooks and the config of a nested repo, outside the sandbox.
    /// </summary>
    private static List<string> NestedGitEntries(string workTree)
    {
        List<string> found = [];
        if (!Directory.Exists(workTree))
        {
            return found;
        }

        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
        var root = new DirectoryInfo(workTree);
        Stack<DirectoryInfo> pending = new([root]);
        while (pending.Count is not 0)
        {
            DirectoryInfo directory = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = directory.GetFileSystemInfos("*", options);
            }
            catch (UnauthorizedAccessException)
            {
                found.Add($"{Path.GetRelativePath(workTree, directory.FullName)} (unreadable)");
                continue;
            }

            foreach (FileSystemInfo entry in entries)
            {
                if (entry.Name is ".git")
                {
                    if (!ReferenceEquals(directory, root))
                    {
                        found.Add(Path.GetRelativePath(workTree, entry.FullName));
                    }
                }
                else if (entry is DirectoryInfo subdirectory && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(subdirectory);
                }
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
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
