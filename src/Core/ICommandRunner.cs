namespace Core;

/// <summary>Runs commands in child processes and returns their exit code and output.</summary>
public interface ICommandRunner
{
    /// <summary>Runs the command. A non-zero exit code is a result, never an exception.</summary>
    Task<CommandResult> RunAsync(Command command, CancellationToken cancellationToken = default);
}
