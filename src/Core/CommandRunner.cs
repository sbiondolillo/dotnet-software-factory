using System.Diagnostics;

namespace Core;

/// <summary>The one place in the library that starts a program.</summary>
public class CommandRunner
{
    /// <summary>
    /// Runs the program with its arguments, in the given working directory and with the given
    /// extra environment variables, and returns its exit code, its standard output and its
    /// standard error. A non-zero exit code is data, not failure. When the program is still
    /// running when the timeout passes, it is stopped and a <see cref="TimeoutException"/> is
    /// thrown.
    /// </summary>
    public virtual async Task<CommandResult> RunAsync(
        string program,
        string[] arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? extraEnvironment,
        TimeSpan timeout)
    {
        // A bare program name is resolved against the parent's PATH before the child's
        // environment is applied: an extra variable may replace the child's PATH.
        string fileName = ResolveProgram(program);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // StartInfo.Environment begins as a copy of this process's variables, so the extra
        // variables are added on top and everything else is inherited.
        if (extraEnvironment is not null)
        {
            foreach (KeyValuePair<string, string> variable in extraEnvironment)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Starting '{program}' failed.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Starting '{program}' failed: {ex.Message}", ex);
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(timeout);

        // Both streams are read concurrently: reading one to the end before reading the
        // other deadlocks when both hold more than a pipe buffer.
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellation.Token);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellation.Token);
        Task exited = process.WaitForExitAsync(cancellation.Token);
        Task deadline = Task.Delay(Timeout.Infinite, cancellation.Token);

        Task completed = await Task.WhenAny(standardOutput, standardError, exited, deadline).ConfigureAwait(false);
        if (completed == deadline)
        {
            throw await TimedOutAsync(program, timeout, process, standardOutput, standardError, exited).ConfigureAwait(false);
        }

        try
        {
            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);
            await exited.ConfigureAwait(false);
            return new CommandResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            throw await TimedOutAsync(program, timeout, process, standardOutput, standardError, exited).ConfigureAwait(false);
        }
    }

    private static async Task<TimeoutException> TimedOutAsync(
        string program,
        TimeSpan timeout,
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
        Task exited)
    {
        await StopAsync(process).ConfigureAwait(false);

        // The stopped process's streams now complete, cancelled or not; observe them so a
        // cancelled read never surfaces as an unobserved task exception.
        try
        {
            await Task.WhenAll(standardOutput, standardError, exited).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: the reads were cancelled with the timeout.
        }
        catch (IOException)
        {
            // Expected: the streams closed with the process.
        }

        return new TimeoutException($"Running '{program}' timed out after {timeout.TotalSeconds:0.##} seconds.");
    }

    private static string ResolveProgram(string program)
    {
        if (program.Contains('/'))
        {
            return program;
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, program);
            if (File.Exists(candidate) && IsExecutable(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"The program '{program}' was not found on the PATH.");
    }

    private static bool IsExecutable(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }

        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
    }
}
