using Core;

namespace Specs;

/// <summary>A caller that runs one command through a runner and returns its exit code.</summary>
public sealed class ExitCodeCaller
{
    private readonly ICommandRunner _runner;
    private readonly Command _command;

    public ExitCodeCaller(ICommandRunner runner, Command command)
    {
        _runner = runner;
        _command = command;
    }

    public async Task<int> RunAsync()
    {
        CommandResult result = await _runner.RunAsync(_command).ConfigureAwait(false);
        return result.ExitCode;
    }
}
