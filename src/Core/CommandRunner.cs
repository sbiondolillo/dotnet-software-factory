using System.ComponentModel;
using System.Diagnostics;

namespace Core;

/// <summary>Starts a program, waits for it with an optional timeout, and captures its exit code and output.</summary>
public class CommandRunner
{
    /// <summary>Runs <paramref name="program"> with its arguments, in <paramref name="workingDirectory"/>, with the extra
    /// <paramref name="environment"/> variables, and stops it after <paramref name="timeout"/> if it is still running.
    /// Throws TimeoutException when the timeout passes, and an exception naming the program when it is on no PATH.</summary>
    public virtual async Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string>? arguments = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = program,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        var process = new Process { StartInfo = startInfo };
        using var cancellation = new CancellationTokenSource();
        if (timeout is not null)
        {
            cancellation.CancelAfter(timeout.Value);
        }

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"Program '{program}' was not found on PATH");
        }

        // Read both streams concurrently, or a full pipe blocks the child and the run never ends.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw new TimeoutException($"Program '{program}' did not finish within {FormatTimeout(timeout!.Value)}");
        }

        string output = await stdout;
        string error = await stderr;
        return new CommandResult(process.ExitCode, output, error);
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        if (timeout.TotalSeconds >= 1)
        {
            long seconds = (long)timeout.TotalSeconds;
            return $"{seconds} {(seconds == 1 ? "second" : "seconds")}";
        }

        return $"{(long)timeout.TotalMilliseconds} milliseconds";
    }
}
