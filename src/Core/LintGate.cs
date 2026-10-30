using System.Globalization;

namespace Core;

/// <summary>Formats a work tree with <c>dotnet format</c> through a <see cref="CommandHost"/> and commits the result.</summary>
public sealed class LintGate(CommandHost host, Git git, TextWriter? log = null)
{
    /// <summary>Formats <paramref name="workTree"/> and commits any change; returns true when a commit was made.</summary>
    public async Task<bool> RunAsync(
        string workTree,
        string message,
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken = default)
    {
        CommandResult format = await host.RunAsync("dotnet", ["format", "--no-restore"], [], cancellationToken).ConfigureAwait(false);
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
