namespace Core;

/// <summary>Formats a work tree in a <see cref="Sandbox"/> and commits every change through a <see cref="Git"/>.</summary>
public sealed class LintGate(Sandbox sandbox, Git git)
{
    /// <summary>Runs <c>dotnet format</c>, then commits every change of <paramref name="workTree"/>. Returns true when a commit was made.</summary>
    public async Task<bool> RunAsync(string workTree, string message, string authorName, string authorEmail, CancellationToken cancellationToken = default)
    {
        CommandResult result = await sandbox.RunAsync("dotnet", ["format", "--no-restore"], [], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode is not 0)
        {
            throw new LintGateException(result.ExitCode, result.StandardError);
        }

        if (await git.HasChangesAsync(workTree, cancellationToken).ConfigureAwait(false) is false)
        {
            return false;
        }

        await git.CommitAllAsync(workTree, message, authorName, authorEmail, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
