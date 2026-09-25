using System.Diagnostics;

namespace Core;

/// <summary>The one place in the library that starts a program.</summary>
public class CommandRunner
{
    public virtual async Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? extraEnvironmentVariables,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = program,
            WorkingDirectory = workingDirectory ?? "",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (extraEnvironmentVariables is not null)
        {
            foreach (var (key, value) in extraEnvironmentVariables)
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
            throw new InvalidOperationException($"Failed to start '{program}': {ex.Message}", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync();

        var completed = await Task.WhenAny(waitTask, Task.Delay(timeout)).ConfigureAwait(false);

        if (completed != waitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have exited between the timeout check and here; ignore.
            }

            throw new TimeoutException(
                $"The program '{program}' did not exit within the timeout of {(int)timeout.TotalSeconds} seconds.");
        }

        var standardOutput = await stdoutTask.ConfigureAwait(false);
        var standardError = await stderrTask.ConfigureAwait(false);

        return new CommandResult(process.ExitCode, standardOutput, standardError);
    }
}
