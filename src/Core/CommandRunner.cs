using System.Diagnostics;

namespace Core;

/// <summary>The outcome of running an external program to completion.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs an external program and captures its exit code, standard output and standard error.</summary>
public class CommandRunner
{
    public virtual async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
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
        catch (Exception ex) when (ex is not TimeoutException)
        {
            throw new InvalidOperationException($"Failed to start '{fileName}': {ex.Message}", ex);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        if (timeout is null)
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new CommandResult(
                process.ExitCode,
                TrimTrailingNewline(await standardOutputTask.ConfigureAwait(false)),
                TrimTrailingNewline(await standardErrorTask.ConfigureAwait(false)));
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout.Value);

        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync(timeoutCts.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have exited between the timeout firing and the kill call.
            }

            throw new TimeoutException($"Command '{fileName}' timed out after {timeout.Value.TotalSeconds:0} seconds");
        }

        return new CommandResult(
            process.ExitCode,
            TrimTrailingNewline(await standardOutputTask.ConfigureAwait(false)),
            TrimTrailingNewline(await standardErrorTask.ConfigureAwait(false)));
    }

    /// <summary>
    /// Strips exactly one trailing newline, matching the convention shell command
    /// substitution uses to capture a program's output.
    /// </summary>
    private static string TrimTrailingNewline(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        if (text.EndsWith('\n'))
        {
            return text[..^1];
        }

        return text;
    }
}
