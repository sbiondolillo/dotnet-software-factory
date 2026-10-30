namespace Core;

/// <summary>Where the gates run their commands over a work tree: a <see cref="Sandbox"/> container, or the host through <see cref="HostShell"/>.</summary>
public abstract class CommandHost
{
    /// <summary>The path of the work tree as a command sees it.</summary>
    public abstract string WorkDirectory { get; }

    /// <summary>A path for a scratch directory named <paramref name="name"/>, outside the work tree, as a command sees it.</summary>
    public abstract string ScratchPath(string name);

    /// <summary>Runs <paramref name="program"/> in the work tree, holding each variable of <paramref name="names"/> by name.</summary>
    public Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default) =>
        RunAsync(program, arguments, names, null, cancellationToken);

    /// <summary>Runs <paramref name="program"/> in <paramref name="workingDirectory"/>, or in the work tree when it is null.</summary>
    public abstract Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> names,
        string? workingDirectory,
        CancellationToken cancellationToken = default);
}
