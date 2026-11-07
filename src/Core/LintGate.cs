using System.Globalization;

namespace Core;

/// <summary>Formats a work tree with <c>dotnet format</c> through a <see cref="Sandbox"/> and commits the result.
/// For a sandbox whose <see cref="Sandbox.NeedsRestore"/> is true, it runs <c>dotnet restore --locked-mode</c> first.</summary>
public sealed class LintGate(Sandbox sandbox, Git git, TextWriter? log = null)
{
    /// <summary>The name of the lint in the job summary and the progress lines.</summary>
    internal const string Name = "lint";

    /// <summary>Formats <paramref name="workTree"/> and commits any change; returns true when a commit was made.</summary>
    public async Task<bool> RunAsync(
        string workTree,
        string message,
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken = default)
    {
        if (sandbox.NeedsRestore)
        {
            CommandResult restore = await sandbox.RunAsync("dotnet", ["restore", "--locked-mode"], [], cancellationToken).ConfigureAwait(false);
            await ProgressLine.WriteAsync(log, string.Create(CultureInfo.InvariantCulture, $"command: dotnet restore --locked-mode exited {restore.ExitCode}")).ConfigureAwait(false);
            if (restore.ExitCode is not 0)
            {
                // The restore writes its errors, such as NU1004, to standard output.
                throw new LintGateException(
                    restore.ExitCode,
                    $"standard output: {restore.StandardOutput}\nstandard error: {restore.StandardError}",
                    "dotnet restore --locked-mode, before the format,");
            }
        }

        CommandResult format = await sandbox.RunAsync("dotnet", ["format", "--no-restore"], [], cancellationToken).ConfigureAwait(false);
        await ProgressLine.WriteAsync(log, string.Create(CultureInfo.InvariantCulture, $"command: dotnet format --no-restore exited {format.ExitCode}")).ConfigureAwait(false);
        if (format.ExitCode is not 0)
        {
            throw new LintGateException(format.ExitCode, format.StandardError);
        }

        if (!await git.HasChangesAsync(workTree, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await git.CommitAllAsync(workTree, message, authorName, authorEmail, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
