using System.Diagnostics;
using System.Globalization;

namespace Core;

/// <summary>An <see cref="ICommandRunner"/> built on <see cref="Process"/>.</summary>
public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(Command command, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.Name,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (command.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        if (command.ExtraEnvironment is not null)
        {
            // startInfo.Environment starts as a copy of this process's
            // environment, so an extra variable replaces the same-named
            // inherited one and an omitted one keeps its inherited value.
            foreach (var (name, value) in command.ExtraEnvironment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        // A program on no PATH makes Process.Start throw; that failure is the
        // contract, so it propagates.
        process.Start();

        // The reads run to the end of the streams even after a timeout kill,
        // so they take no token.
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var exited = process.WaitForExitAsync(cancellationToken);

        if (command.Timeout is { } timeout)
        {
            using var delay = Task.Delay(timeout, cancellationToken);
            var finished = await Task.WhenAny(exited, delay).ConfigureAwait(false);
            if (finished != exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the timeout and the kill.
                }

                await exited.ConfigureAwait(false);
                await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
                throw new TimeoutException($"Command {command.Name} timed out after {FormatDuration(timeout)}.");
            }
        }
        else
        {
            await exited.ConfigureAwait(false);
        }

        await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        return new CommandResult(process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var seconds = duration.TotalSeconds;
        return seconds == 1 ? "1 second" : $"{seconds.ToString("0.##", CultureInfo.InvariantCulture)} seconds";
    }
}
