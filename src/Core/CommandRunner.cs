using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Core;

/// <summary>The command <see cref="CommandRunner.RunAsync"/> starts and reads.</summary>
/// <param name="Program">The executable to start, resolved on the PATH.</param>
/// <param name="Arguments">The arguments; each one reaches the program verbatim, with no shell in between.</param>
/// <param name="WorkingDirectory">The directory the program runs in, or the current directory when null.</param>
/// <param name="EnvironmentVariables">Extra environment variables, layered on the inherited environment.</param>
/// <param name="Timeout">How long the command may run before it is killed as a whole process tree.</param>
public sealed record CommandRequest(
    string Program,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    TimeSpan? Timeout = null);

/// <summary>The exit code and the raw, untrimmed output streams of a finished command.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Starts a program and reads its exit code and its output streams.</summary>
public static class CommandRunner
{
    private const int SigPipe = 13;

    private static readonly object ProcessStartLock = new();

    public static async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Program,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        if (request.EnvironmentVariables is { Count: > 0 })
        {
            foreach (var (name, value) in request.EnvironmentVariables)
            {
                startInfo.Environment[name] = value;
            }
        }

        Process? process = null;
        try
        {
            process = StartWithDefaultSigpipe(startInfo, request.Program);

            // Read both streams before waiting for the exit, so a full pipe
            // buffer cannot deadlock the child. The reads are tied to the
            // process, not to the caller's cancellation.
            var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await WaitForExitAsync(process, request, cancellationToken);

            return new CommandResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (Exception exception) when (process is null && exception is not TimeoutException)
        {
            throw new InvalidOperationException($"Program '{request.Program}' could not be started: {exception.Message}", exception);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static Process StartWithDefaultSigpipe(ProcessStartInfo startInfo, string program)
    {
        // The .NET host runs with SIGPIPE ignored, and a child inherits that: it then survives a
        // broken pipe and prints to standard error instead of dying. Give the child the standard
        // disposition, the way a shell would, and restore the host's afterwards.
        lock (ProcessStartLock)
        {
            var previous = NativeMethods.Signal(SigPipe, IntPtr.Zero);
            try
            {
                return Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"Program '{program}' could not be started.");
            }
            finally
            {
                _ = NativeMethods.Signal(SigPipe, previous);
            }
        }
    }

    private static async Task WaitForExitAsync(Process process, CommandRequest request, CancellationToken cancellationToken)
    {
        if (request.Timeout is not { } timeout)
        {
            await process.WaitForExitAsync(cancellationToken);
            return;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var exit = process.WaitForExitAsync(timeoutSource.Token);
        var completed = await Task.WhenAny(exit, Task.Delay(timeout, timeoutSource.Token));
        if (!ReferenceEquals(completed, exit))
        {
            timeoutSource.Cancel();
            KillProcessTree(process);
            throw new TimeoutException($"Program '{request.Program}' timed out after {RenderTimeout(timeout)}.");
        }

        await exit;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and the kill.
        }
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "signal")]
        public static extern int Signal(int signum, IntPtr handler);
    }

    private static string RenderTimeout(TimeSpan timeout)
    {
        var seconds = timeout.TotalSeconds;
        if (seconds == 1)
        {
            return "1 second";
        }

        return seconds % 1 == 0 ? $"{(int)seconds} seconds" : $"{seconds} seconds";
    }
}
