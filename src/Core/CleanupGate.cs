using System.Globalization;

namespace Core;

/// <summary>The scope, lost-test and check rules of a cleanup run, shared by <see cref="FactoryRun"/> and <see cref="FeedbackRun"/>.</summary>
internal static class CleanupGate
{
    public static async Task<IReadOnlyList<string>> ScopeFaultsAsync(
        Git git,
        string baseCommit,
        string directory,
        IReadOnlyList<string> scope,
        CancellationToken cancellationToken)
    {
        string[] changed = await git.ListChangedFilesAsync(baseCommit, directory, cancellationToken).ConfigureAwait(false);
        List<string> faults = [];
        foreach (string path in changed.Where(path => !path.StartsWith(".factory/", StringComparison.Ordinal)))
        {
            if (path.StartsWith("specs/features/", StringComparison.Ordinal))
            {
                faults.Add($"pin: {path}");
            }
            else if (!CleanupBody.InScope(scope, path))
            {
                faults.Add($"outside the scope: {path}");
            }
        }

        return faults;
    }

    public static bool PinExempt(IReadOnlyList<string> scope, string path) =>
        CleanupBody.InScope(scope, path) && !path.StartsWith("specs/features/", StringComparison.Ordinal);

    public static IReadOnlyList<string> LostTests(IReadOnlySet<string> basePassed, TestGateResult gate)
    {
        HashSet<string> nowPassed = [.. (gate.Tests ?? []).Where(test => test.Outcome is TestOutcome.Passed).Select(test => $"{test.ClassName}.{test.Method}")];
        return [.. basePassed.Where(name => !nowPassed.Contains(name)).Order(StringComparer.Ordinal).Select(name => $"lost test: {name}")];
    }

    public static string CheckRedo(CommandResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"The check command exited with the code {result.ExitCode}\nstandard output: {result.StandardOutput}\nstandard error: {result.StandardError}");

    public static async Task<CommandResult> RunCheckAsync(Sandbox sandbox, string command, TextWriter? progress, CancellationToken cancellationToken, TextWriter? log = null)
    {
        await ProgressLine.WriteAsync(progress, "check: start").ConfigureAwait(false);
        string copy = $"/tmp/check-{Guid.NewGuid():N}";
        CommandResult add = await sandbox.RunAsync("git", ["worktree", "add", "--detach", copy, "HEAD"], [], cancellationToken).ConfigureAwait(false);
        if (add.ExitCode is not 0)
        {
            throw new SandboxException(add.StandardError);
        }

        try
        {
            CommandResult ran = await sandbox.RunAsync("sh", ["-c", command], [], copy, cancellationToken).ConfigureAwait(false);
            await ProgressLine.WriteAsync(log, string.Create(CultureInfo.InvariantCulture, $"command: check exited {ran.ExitCode}")).ConfigureAwait(false);
            return ran;
        }
        finally
        {
            await sandbox.RunAsync("git", ["worktree", "remove", "--force", copy], [], CancellationToken.None).ConfigureAwait(false);
        }
    }
}
