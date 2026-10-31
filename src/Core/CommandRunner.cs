using System.Diagnostics;
using System.Globalization;

namespace Core;

/// <summary>Runs a program and captures its exit code and standard streams.</summary>
public class CommandRunner
{
    /// <summary>Runs the program <paramref name="spec"/> describes and captures its result.</summary>
    public virtual async Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var psi = new ProcessStartInfo
        {
            FileName = spec.Command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in spec.Arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (spec.WorkingDirectory is not null)
        {
            psi.WorkingDirectory = spec.WorkingDirectory;
        }

        foreach (KeyValuePair<string, string> extra in spec.EnvironmentVariables)
        {
            psi.Environment[extra.Key] = extra.Value;
        }

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("the process did not start");
        }
        catch (Exception ex) when (ex is not CommandStartException)
        {
            throw new CommandStartException(spec.Command, ex);
        }

        using (process)
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            if (spec.Timeout is { } timeout)
            {
                using var cts = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
                try
                {
                    await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    StopProcessTree(process);
                    await stdoutTask.ConfigureAwait(false);
                    await stderrTask.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    throw new TimeoutException($"{spec.Command} did not finish within {FormatSeconds(timeout)}");
                }
            }
            else
            {
                try
                {
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    StopProcessTree(process);
                    await stdoutTask.ConfigureAwait(false);
                    await stderrTask.ConfigureAwait(false);
                    throw;
                }
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
            };
        }
    }

    /// <summary>Stops the program and everything it started.</summary>
    private static void StopProcessTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and the kill.
        }
    }

    private static string FormatSeconds(TimeSpan timeout)
    {
        string seconds = timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return $"{seconds} {(seconds == "1" ? "second" : "seconds")}";
    }
}
