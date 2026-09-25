namespace Core;

/// <summary>
/// A caller of <see cref="CommandRunner"/>: it forwards every run to the runner it was
/// given, so a test of a caller can hold a mock of the runner and start no program.
/// </summary>
public sealed class CommandCaller
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly CommandRunner _runner;

    /// <summary>Creates a caller that forwards its runs to the given runner.</summary>
    public CommandCaller(CommandRunner runner) => _runner = runner;

    /// <summary>Runs the program with its arguments through the held runner.</summary>
    public Task<CommandResult> RunAsync(string program, string[] arguments) =>
        _runner.RunAsync(program, arguments, null, null, DefaultTimeout);
}
