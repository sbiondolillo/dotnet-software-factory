using System.Diagnostics;

namespace Core;

/// <summary>Runs a program and returns its exit code and its standard output
/// and standard error, captured whole. A class, not sealed, and its
/// <see cref="RunAsync"/> is virtual so a test can mock it directly.</summary>
public class CommandRunner
{
    public virtual async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Program,
            WorkingDirectory = request.WorkingDirectory ?? "",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in request.EnvironmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start program \"{request.Program}\": {ex.Message}", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);

        if (request.Timeout is { } timeout)
        {
            var completed = await Task.WhenAny(exitTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            if (completed != exitTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process already exited between the timeout firing and the kill.
                }

                throw new TimeoutException(
                    $"The program \"{request.Program}\" did not exit within {timeout.TotalSeconds} second(s).");
            }
        }

        await exitTask.ConfigureAwait(false);
        var standardOutput = await stdoutTask.ConfigureAwait(false);
        var standardError = await stderrTask.ConfigureAwait(false);

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
        };
    }
}
