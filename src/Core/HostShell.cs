namespace Core;

/// <summary>Runs the gates' commands on the host over <paramref name="workTree"/>, with a cleared environment that keeps an allowlist
/// and <paramref name="secretNames"/> when a command asks for them.</summary>
public sealed class HostShell(CommandRunner runner, string workTree, IReadOnlyList<string> secretNames) : CommandHost
{
    internal CommandRunner Runner { get; } = runner;

    internal IReadOnlyList<string> SecretNames { get; } = secretNames;

    /// <inheritdoc />
    public override string WorkDirectory => workTree;

    /// <inheritdoc />
    public override string ScratchPath(string name) => throw new NotImplementedException();

    /// <inheritdoc />
    public override Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> names,
        string? workingDirectory,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
