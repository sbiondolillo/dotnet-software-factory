using System.ComponentModel;
using System.Diagnostics;

namespace Core;

/// <summary>
/// The captured result of running an external program: its exit code,
/// its standard output and its standard error.
/// </summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs an external program and reports its exit code, standard output and
/// standard error. Each argument reaches the program's argv unchanged, the
/// program's environment is this process's environment plus any extra
/// variables, and a timeout kills the whole process tree.
/// </summary>
public class CommandRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> in <paramref name="workingDirectory"/>
    /// with <paramref name="arguments"/> and waits for it to exit.
    /// </summary>
    /// <param name="workingDirectory">The directory the program runs in.</param>
    /// <param name="fileName">The program to start.</param>
    /// <param name="arguments">The program's arguments, each one argv entry.</param>
    /// <param name="extraEnvironmentVariables">
    /// Variables added to (or overriding) the inherited environment.
    /// </param>
    /// <param name="timeout">
    /// How long to wait before killing the whole process tree and throwing
    /// a <see cref="TimeoutException"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait without killing the program.</param>
    /// <returns>The program's exit code, standard output and standard error.</returns>
    /// <exception cref="TimeoutException">
    /// The program did not exit within <paramref name="timeout"/>; it was killed.
    /// </exception>
    /// <exception cref="Win32Exception">The program could not be started.</exception>
    public virtual async Task<CommandResult> RunAsync(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? extraEnvironmentVariables = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // ArgumentList passes each element as its own argv entry: quotes,
        // spaces and dollar signs reach the program unchanged, with no shell
        // re-parsing on either side.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // ProcessStartInfo.Environment starts seeded from this process's
        // environment, so every inherited variable survives and only the
        // named extras are added or overridden.
        if (extraEnvironmentVariables is not null)
        {
            foreach (var (name, value) in extraEnvironmentVariables)
            {
                startInfo.Environment[name] = value;
            }
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Process.Start returned no process for '{fileName}'.");
        }
        catch (Win32Exception ex)
        {
            // Name the program in the message rather than relying on the
            // runtime's own wording for the failure.
            throw new Win32Exception(ex.NativeErrorCode, $"Could not start '{fileName}': {ex.Message}");
        }

        // Read both streams concurrently with the wait so a full pipe buffer
        // on either one can't deadlock the run.
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = new CancellationTokenSource();
        if (timeout is { } span)
        {
            timeoutSource.CancelAfter(span);
        }

        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, cancellationToken);

        try
        {
            await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            // Kill the whole tree so the program's own children die with it.
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"'{fileName}' did not exit within {timeout!.Value.TotalSeconds} second(s) and was killed.");
        }

        var stdout = await standardOutput.ConfigureAwait(false);
        var stderr = await standardError.ConfigureAwait(false);
        return new CommandResult(process.ExitCode, stdout, stderr);
    }
}
