using System.Diagnostics;

namespace Core;

/// <summary>The outcome of a finished program: its exit code and captured streams.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Everything needed to start a program: its name, arguments, working directory,
/// extra environment variables and an optional timeout.</summary>
public sealed record CommandRequest(
    string Program,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    TimeSpan? Timeout);

/// <summary>The seam the rest of the library starts programs through. Callers test against a mock of it.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(CommandRequest request);
}

/// <summary>The one place the library starts a program.</summary>
public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(CommandRequest request)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            FileName = request.Program,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in request.Arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (request.WorkingDirectory is not null)
        {
            psi.WorkingDirectory = request.WorkingDirectory;
        }

        // The child inherits the caller's environment by default; the request's
        // variables are written on top, so an extra replaces an inherited one of
        // the same name and the rest keep their inherited values.
        foreach (var (name, value) in request.EnvironmentVariables)
        {
            psi.Environment[name] = value;
        }

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Cannot start '{request.Program}': the process did not start.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Cannot start '{request.Program}': {ex.Message}", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (request.Timeout is { } timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                process.Dispose();
                throw new TimeoutException($"'{request.Program}' timed out after {FormatTimeout(timeout)}.");
            }
        }
        else
        {
            await process.WaitForExitAsync();
        }

        var standardOutput = await stdout;
        var standardError = await stderr;
        var exitCode = process.ExitCode;
        process.Dispose();
        return new CommandResult(exitCode, standardOutput, standardError);
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        var seconds = (int)timeout.TotalSeconds;
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }
}
