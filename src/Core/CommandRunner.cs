using System.ComponentModel;
using System.Diagnostics;

namespace Core;

/// <summary>Runs an external program and captures its exit code and output.</summary>
public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Failed to start program '{program}': {ex.Message}", ex);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var processExitTask = process.WaitForExitAsync();

        if (timeout is { } timeoutValue)
        {
            var delayTask = Task.Delay(timeoutValue);
            var completed = await Task.WhenAny(processExitTask, delayTask).ConfigureAwait(false);
            if (completed == delayTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process already exited between the race and the kill.
                }

                throw new TimeoutException(
                    $"Program '{program}' did not exit within {timeoutValue:c}.");
            }
        }
        else
        {
            await processExitTask.ConfigureAwait(false);
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        return new CommandResult(process.ExitCode, standardOutput, standardError);
    }
}
