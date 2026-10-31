using System.Collections;

namespace Core;

/// <summary>Runs the gates' commands on the host over <paramref name="workTree"/>, with a cleared environment that keeps an allowlist
/// and the names that each command asks for.</summary>
/// <remarks>
/// A gate runs code that an agent wrote: MSBuild targets, tests and checks. So each command starts from an empty environment, and
/// gets back only <c>PATH</c>, <c>HOME</c>, <c>TMPDIR</c>, <c>LANG</c>, each <c>DOTNET_*</c> and <c>NUGET_*</c> variable, and each
/// name that the command passes. No job secret reaches a command that does not name it.
/// </remarks>
public sealed class HostShell(CommandRunner runner, string workTree) : CommandHost
{
    private static readonly string[] Allowlist = ["PATH", "HOME", "TMPDIR", "LANG"];

    private static readonly string[] AllowedPrefixes = ["DOTNET_", "NUGET_"];

    internal CommandRunner Runner { get; } = runner;

    /// <inheritdoc />
    public override string WorkDirectory => workTree;

    /// <inheritdoc />
    public override string ScratchPath(string name) => Path.Combine(Path.GetTempPath(), name);

    /// <inheritdoc />
    public override Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> names,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> environment = CopyEnvironment(names);
        if (program is "git")
        {
            // Git runs the hooks and the config of a nested repo, so a git command does not start over one.
            List<string> nested = Git.NestedGitEntries(workTree);
            if (nested.Count is not 0)
            {
                throw new GitCommandException(
                    arguments.Count is 0 ? "" : arguments[0],
                    $"the work tree holds a .git other than its own, or a directory that cannot be read: {string.Join(", ", nested)}");
            }

            Git.TurnHooksOff(environment);
        }

        return Runner.RunAsync(
            new CommandSpec
            {
                Command = program,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? workTree,
                ClearEnvironment = true,
                EnvironmentVariables = environment,
            },
            cancellationToken);
    }

    private static Dictionary<string, string> CopyEnvironment(IReadOnlyList<string> names)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value
                && (Allowlist.Contains(name, StringComparer.Ordinal) || AllowedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))))
            {
                environment[name] = value;
            }
        }

        foreach (string name in names)
        {
            if (Environment.GetEnvironmentVariable(name) is string value)
            {
                environment[name] = value;
            }
        }

        return environment;
    }
}
