using System.Diagnostics;

namespace Core;

/// <summary>
/// The one place in the library that starts a program. <see cref="RunAsync"/> starts the program
/// without a shell, reads its standard output and standard error concurrently, and returns a
/// <see cref="CommandResult"/> holding the exit code and both streams. A timeout that passes
/// before the program exits stops the program and throws a <see cref="TimeoutException"/> naming
/// the program and the timeout; a program that cannot start throws an exception naming the program.
/// </summary>
public class CommandRunner
{
    /// <summary>Starts the program and returns its exit code and output.</summary>
    /// <param name="program">The program to start, resolved on the PATH.</param>
    /// <param name="arguments">
    /// The arguments, each passed character for character; no shell is involved.
    /// </param>
    /// <param name="workingDirectory">The directory the program runs in, when given.</param>
    /// <param name="extraEnvironmentVariables">
    /// Variables visible to the program; each one replaces an inherited variable of the same name,
    /// and the inherited variables it leaves out stay visible.
    /// </param>
    /// <param name="timeout">
    /// The time the program is given; when it passes with the program still running, the program is
    /// stopped and a <see cref="TimeoutException"/> is thrown.
    /// </param>
    public virtual async Task<CommandResult> RunAsync(
        string program,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? extraEnvironmentVariables = null,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = program,
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

        if (extraEnvironmentVariables is not null)
        {
            foreach (var (name, value) in extraEnvironmentVariables)
            {
                startInfo.Environment[name] = value;
            }
        }

        var process = new Process { StartInfo = startInfo };
        Exception? startFailure = null;
        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception exception)
        {
            startFailure = exception;
            started = false;
        }

        if (!started)
        {
            process.Dispose();
            throw new InvalidOperationException($"The program '{program}' could not be started.", startFailure);
        }

        try
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            if (timeout is { } duration)
            {
                using var cancellation = new CancellationTokenSource(duration);
                try
                {
                    await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    StopProcessTree(process);
                    throw new TimeoutException(
                        $"The program '{program}' was still running after {FormatTimeout(duration)}.");
                }
            }
            else
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            var streams = await Task.WhenAll(new[] { stdoutTask, stderrTask }).ConfigureAwait(false);
            return new CommandResult(process.ExitCode, streams[0], streams[1]);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void StopProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // The process had already exited.
        }
    }

    private static string FormatTimeout(TimeSpan duration)
    {
        if (duration.TotalSeconds % 1 == 0)
        {
            return $"{(long)duration.TotalSeconds} seconds";
        }

        return $"{duration.TotalSeconds:0.###} seconds";
    }
}
