using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Core;

/// <summary>
/// Runs an external program with a timeout and returns its exit code and captured output.
/// Not sealed and with a parameterless constructor so a caller under test can mock the class itself.
/// </summary>
public class CommandRunner
{
    public virtual async Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = program,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // One ArgumentList.Add per argument: each value reaches argv as one entry,
        // unquoted and untouched, so no shell quoting is ever applied.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            // Set on top of the inherited environment: variables the caller leaves
            // out keep their inherited values.
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)!;
        }
        catch (Win32Exception exception)
        {
            // .NET on Linux reports "No such file or directory" without the
            // program name, so name it here.
            throw new InvalidOperationException($"could not start {program}: {exception.Message}", exception);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            // Both reads are already in flight, so neither pipe buffer fills and
            // deadlocks a child that writes a lot to both streams.
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Kill the whole tree: a shell such as dash forks its child instead
            // of exec-ing it, so killing the shell alone leaves the child running.
            process.Kill(true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            throw new TimeoutException($"process {program} did not finish within {timeout.TotalSeconds} seconds");
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        return new CommandResult(process.ExitCode, stdout, stderr);
    }
}
