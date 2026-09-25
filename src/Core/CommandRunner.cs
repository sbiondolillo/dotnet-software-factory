using System.Diagnostics;

namespace Core;

/// <summary>Runs a program with a timeout and returns its exit code and captured output.</summary>
public class CommandRunner
{
    /// <summary>
    /// Starts <paramref name="program"/> with <paramref name="arguments"/> and waits for it to exit,
    /// honouring an optional <paramref name="timeout"/>.
    /// </summary>
    /// <param name="program">The program to start, found on the PATH when it is not an absolute path.</param>
    /// <param name="arguments">The arguments the program receives, each one arriving as a single argument.</param>
    /// <param name="workingDirectory">The directory the program runs in, when given.</param>
    /// <param name="extraEnvironmentVariables">
    /// Extra environment variables the program sees. An entry whose name matches an inherited variable
    /// replaces that variable's value; an inherited name the entries leave out stays visible with its
    /// inherited value.
    /// </param>
    /// <param name="timeout">The time to wait for the program to exit, when given.</param>
    /// <returns>
    /// The exit code and the captured standard output and standard error. The captured streams have
    /// their trailing line endings trimmed: a program that writes "hello world\n" yields "hello world",
    /// and one that writes exactly N characters with no trailing newline still yields N characters.
    /// </returns>
    /// <exception cref="TimeoutException">
    /// The program was still running when the timeout passed. The process tree is killed first.
    /// </exception>
    /// <exception cref="InvalidOperationException">The program could not be started, for example because it is on no PATH.</exception>
    public virtual async Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
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

        // ArgumentList, not a shell string: the process is started directly, never wrapped in a shell,
        // so an argument containing spaces, quotes or $ arrives character for character.
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

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Could not start program '{program}': {e.Message}", e);
        }

        if (process is null)
        {
            throw new InvalidOperationException($"Could not start program '{program}': the process did not start.");
        }

        var outputLock = new object();
        var standardOutputLines = new List<string>();
        var standardErrorLines = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (outputLock)
            {
                standardOutputLines.Add(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (outputLock)
            {
                standardErrorLines.Add(e.Data);
            }
        };

        // Read both streams concurrently so a large write to either cannot fill its pipe buffer
        // and deadlock the program.
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exitTask = process.WaitForExitAsync();
        if (timeout is null)
        {
            await exitTask.ConfigureAwait(false);
        }
        else
        {
            try
            {
                await exitTask.WaitAsync(timeout.Value).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The timeout elapsed before the process exited; the HasExited check below decides.
            }
        }

        if (!process.HasExited)
        {
            // The timeout passed before the program exited: stop the whole process tree and report it.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The program exited between the wait and the kill.
            }

            process.WaitForExit();
            process.Dispose();
            throw new TimeoutException($"Program '{program}' timed out after {timeout!.Value.TotalMilliseconds:0} milliseconds");
        }

        // The exit notification fires before the async output handlers have flushed their final lines.
        // The synchronous wait blocks until they have.
        process.WaitForExit();

        string standardOutput;
        string standardError;
        lock (outputLock)
        {
            standardOutput = string.Join("\n", standardOutputLines).TrimEnd('\r', '\n');
            standardError = string.Join("\n", standardErrorLines).TrimEnd('\r', '\n');
        }

        var exitCode = process.ExitCode;
        process.Dispose();

        return new CommandResult(exitCode, standardOutput, standardError);
    }
}
