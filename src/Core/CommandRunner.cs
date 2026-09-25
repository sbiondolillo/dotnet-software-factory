using System.Diagnostics;

namespace Core;

/// <summary>The one place the library starts a program.</summary>
public class CommandRunner
{
    /// <summary>
    /// Starts the requested program, reads both streams whole while it runs, and returns its
    /// exit code and output. Each argument goes to the child as its own argv entry, never as a
    /// joined string, so a space, a quote or a dollar sign in one argument reaches it unchanged.
    /// </summary>
    public virtual async Task<CommandResult> RunAsync(CommandRequest request)
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

        // StartInfo.Environment overlays the inherited environment: the keys it names replace
        // their inherited values, and every other inherited key is kept as is.
        if (request.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in request.EnvironmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Read both streams while the process runs, so a full pipe can never block the child.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExitAsync();

        if (request.Timeout is not null)
        {
            using var timeout = new CancellationTokenSource(request.Timeout.Value);
            try
            {
                await exited.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                StopTree(process);
                throw new TimeoutException(
                    $"'{request.Program}' was still running after {request.Timeout.Value.TotalSeconds:0} seconds; the process tree was stopped.");
            }
        }
        else
        {
            await exited.ConfigureAwait(false);
        }

        return new CommandResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    private static void StopTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and the kill; there is nothing left to stop.
        }
    }
}
