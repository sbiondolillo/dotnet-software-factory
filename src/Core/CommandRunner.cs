using System.Diagnostics;

namespace Core;

/// <summary>The outcome of running a program: its exit code and captured output.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Everything needed to start and run a program.</summary>
public sealed class CommandSpec
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
    public TimeSpan? Timeout { get; init; }
}

/// <summary>Runs an external program and reports its exit code, stdout and stderr.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken cancellationToken = default);
}

/// <summary>Runs an external program with <see cref="System.Diagnostics.Process"/>.</summary>
public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (spec.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = spec.WorkingDirectory;
        }

        if (spec.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in spec.EnvironmentVariables)
            {
                startInfo.EnvironmentVariables[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to start program \"{spec.FileName}\": {ex.Message}", ex);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        if (spec.Timeout is { } timeout)
        {
            var waitTask = process.WaitForExitAsync(cancellationToken);
            var completed = await Task.WhenAny(waitTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);

            if (completed != waitTask)
            {
                process.Kill(entireProcessTree: true);
                await waitTask.ConfigureAwait(false);

                throw new TimeoutException(
                    $"Program \"{spec.FileName}\" did not exit within {DescribeDuration(timeout)} and was stopped.");
            }
        }
        else
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        return new CommandResult(process.ExitCode, standardOutput, standardError);
    }

    private static string DescribeDuration(TimeSpan duration)
    {
        var seconds = (int)duration.TotalSeconds;
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }
}
