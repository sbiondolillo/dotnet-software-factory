using Core;

namespace Specs;

/// <summary>What a workflow step did when it ran: its exit code, its standard error and the text it wrote to the job environment file.</summary>
public sealed record WorkflowStepRun(int ExitCode, string StandardError, string EnvironmentFile);

/// <summary>Finds a step of a workflow and runs its <c>run</c> text as the runner does: <c>bash -e</c> in a work tree, with <c>GITHUB_ENV</c> set.</summary>
public static class WorkflowStep
{
    private const string Lockfile = ".github/agents/claude-lock.json";

    /// <summary>Whether a program of that name is in a directory of the <c>PATH</c>.</summary>
    public static bool OnPath(string program)
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, program)));

    /// <summary>The <c>run</c> text of the step named <paramref name="stepName"/> in a job of the workflow.</summary>
    public static string RunText(string workflowFile, string stepName)
    {
        Dictionary<string, object?> root = WorkflowChecks.Parse(File.ReadAllText(workflowFile));
        Dictionary<string, object?> jobs = root.GetValueOrDefault("jobs") as Dictionary<string, object?> ?? [];
        string? run = jobs.Values
            .OfType<Dictionary<string, object?>>()
            .SelectMany(job => job.GetValueOrDefault("steps") as List<object?> ?? [])
            .OfType<Dictionary<string, object?>>()
            .Where(step => (step.GetValueOrDefault("name") as string) == stepName)
            .Select(step => step.GetValueOrDefault("run") as string)
            .FirstOrDefault();
        return run ?? throw new InvalidOperationException($"{workflowFile} has no step {stepName} with a run command");
    }

    /// <summary>Runs the text in a new work tree that holds the lockfile text at <c>.github/agents/claude-lock.json</c>, and removes the work tree.</summary>
    public static async Task<WorkflowStepRun> RunAsync(string runText, string lockfileText, CancellationToken cancellationToken = default)
    {
        string tree = Directory.CreateTempSubdirectory("workflow-step-").FullName;
        try
        {
            string lockfile = Path.Combine(tree, Lockfile);
            Directory.CreateDirectory(Path.GetDirectoryName(lockfile)!);
            await File.WriteAllTextAsync(lockfile, lockfileText, cancellationToken).ConfigureAwait(false);
            string environmentFile = Path.Combine(tree, "github-env");
            await File.WriteAllTextAsync(environmentFile, "", cancellationToken).ConfigureAwait(false);

            CommandResult result = await new CommandRunner().RunAsync(
                new CommandSpec
                {
                    Command = "bash",
                    Arguments = ["-e", "-c", runText],
                    WorkingDirectory = tree,
                    EnvironmentVariables = new Dictionary<string, string> { ["GITHUB_ENV"] = environmentFile },
                },
                cancellationToken).ConfigureAwait(false);

            string written = await File.ReadAllTextAsync(environmentFile, cancellationToken).ConfigureAwait(false);
            return new WorkflowStepRun(result.ExitCode, result.StandardError, written);
        }
        finally
        {
            Directory.Delete(tree, true);
        }
    }
}
