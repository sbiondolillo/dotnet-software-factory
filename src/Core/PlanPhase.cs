using System.Security.Cryptography;
using System.Text;

namespace Core;

/// <summary>The outcome of <see cref="PlanPhase"/>: passed with a pin, or failed with failure sentences.</summary>
public sealed record PlanPhaseResult(bool Passed, Pin? Pin, IReadOnlyList<string> Failures);

/// <summary>Asks the agent for feature files and a plan, checks the work tree, and pins the scenarios.
/// With <c>scenarios</c>, the feature files the issue wrote, the agent writes the plan alone.</summary>
public sealed class PlanPhase(ClaudeCodeAgent agent, bool refactor = false, IssueScenariosResult? scenarios = null)
{
    private bool PlanOnly => refactor || scenarios is not null;

    private const string FeatureDirectory = "specs/features/";
    private const string PlanPath = ".factory/plan.md";

    private const string IntegrationText =
        "The build suite answers each call to a service with a canned answer from a stub HttpMessageHandler. "
        + "For each canned answer that a scenario depends on, name in the plan one test under tests/Integration/ that gets the same answer from the real service. ";

    /// <summary>Runs the first prompt and, when its gate fails, one correction prompt.</summary>
    public async Task<PlanPhaseResult> RunAsync(
        string title,
        string body,
        string model,
        int maxTurns,
        string workTree,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> baseline = Snapshot(workTree, PlanOnly);
        string prompt = BuildPrompt(title, body);

        (List<string> failures, bool isError) = await RunOnceAsync(prompt, model, maxTurns, workTree, baseline, cancellationToken).ConfigureAwait(false);
        if (isError)
        {
            return new PlanPhaseResult(false, null, failures);
        }

        if (failures.Count is 0)
        {
            return new PlanPhaseResult(true, Pin.Take(workTree), []);
        }

        StringBuilder correction = new StringBuilder(prompt)
            .Append("\n\nYour previous run failed these checks. Fix each one:\n");
        foreach (string failure in failures)
        {
            correction.Append("- ").Append(failure).Append('\n');
        }

        (failures, _) = await RunOnceAsync(correction.ToString(), model, maxTurns, workTree, baseline, cancellationToken).ConfigureAwait(false);
        return failures.Count is 0
            ? new PlanPhaseResult(true, Pin.Take(workTree), [])
            : new PlanPhaseResult(false, null, failures);
    }

    private string BuildPrompt(string title, string body) =>
        scenarios is not null
        ? "Plan the work of the issue below.\n\n"
            + $"Issue title:\n{title}\n\nIssue body:\n{body}\n\n"
            + Named("The issue wrote these feature files, and the build makes their scenarios pass: ", scenarios.Written)
            + Named("The issue removed these feature files: ", scenarios.Removed)
            + $"Write the plan to {PlanPath}, stating each result the build owes. "
            + IntegrationText
            + $"Write no other file: {PlanPath} is the only file the plan phase writes. "
            + "Leave every other file as it is."
        : refactor
        ? "Plan the work of the refactor issue below.\n\n"
            + $"Issue title:\n{title}\n\nIssue body:\n{body}\n\n"
            + $"Write the plan to {PlanPath}, stating the structure the build gives the code and the steps to get there. "
            + $"Write no other file: {PlanPath} is the only file the plan phase writes. "
            + "Leave every other file as it is."
        : "Plan the work of the issue below.\n\n"
        + $"Issue title:\n{title}\n\nIssue body:\n{body}\n\n"
        + $"Write Gherkin scenarios for the behaviour in feature files under {FeatureDirectory} "
        + $"and write the plan to {PlanPath}, stating each result the build owes. "
        + IntegrationText
        + "Remove each feature file whose behaviour the issue removes or replaces. "
        + "Leave every other file as it is.";

    private async Task<(List<string> Failures, bool IsError)> RunOnceAsync(
        string prompt,
        string model,
        int maxTurns,
        string workTree,
        Dictionary<string, string> baseline,
        CancellationToken cancellationToken)
    {
        ClaudeCodeResult answer = await agent.RunAsync(prompt, model, maxTurns, "plan", cancellationToken).ConfigureAwait(false);
        if (answer.IsError)
        {
            return ([string.IsNullOrWhiteSpace(answer.Result) ? "claude answered with an error and no text." : answer.Result], true);
        }

        return (PlanOnly ? RefactorGate(workTree, baseline) : Gate(workTree, baseline), false);
    }

    private static string Named(string lead, IReadOnlyList<string> paths) =>
        paths.Count is 0 ? "" : lead + string.Join(", ", paths) + ". ";

    private static List<string> RefactorGate(string workTree, Dictionary<string, string> baseline)
    {
        var failures = new List<string>();
        Dictionary<string, string> now = Snapshot(workTree, all: true);
        if (!File.Exists(Path.Combine(workTree, PlanPath)))
        {
            failures.Add($"The plan {PlanPath} is missing.");
        }

        foreach (string path in now.Keys.Union(baseline.Keys).Order(StringComparer.Ordinal))
        {
            bool before = baseline.TryGetValue(path, out string? old);
            bool after = now.TryGetValue(path, out string? current);
            string what = !before ? "new" : !after ? "gone" : old != current ? "changed" : "";
            if (what.Length > 0)
            {
                failures.Add($"The file {path} is {what}; write no file outside .factory/ in the plan phase.");
            }
        }

        return failures;
    }

    private static List<string> Gate(string workTree, Dictionary<string, string> baseline)
    {
        var failures = new List<string>();
        Dictionary<string, string> now = Snapshot(workTree, all: false);

        bool featureChanged = now.Any(pair => IsFeature(pair.Key) && (!baseline.TryGetValue(pair.Key, out string? old) || old != pair.Value))
            || baseline.Keys.Any(path => IsFeature(path) && !now.ContainsKey(path));
        if (featureChanged && !now.Keys.Any(IsFeature))
        {
            featureChanged = false;
        }

        if (!featureChanged)
        {
            failures.Add($"No feature file under {FeatureDirectory} is new or changed.");
        }

        foreach (string path in now.Keys.Where(IsFeature).Order(StringComparer.Ordinal))
        {
            try
            {
                if (FeatureFiles.ReadScenarioTitles(Path.Combine(workTree, path)).Count is 0)
                {
                    failures.Add($"The feature file {path} holds no scenario.");
                }

                foreach (int line in FeatureFiles.ReadOverIndentedDocStringLines(path, File.ReadAllText(Path.Combine(workTree, path))))
                {
                    failures.Add($"The feature file {path}: the doc string at line {line} has text indented past its opening delimiter; start the text at the column of its opening delimiter.");
                }
            }
            catch (FeatureFileException exception)
            {
                failures.Add($"The feature file {path} does not parse: {exception.Message}");
            }
        }

        if (!File.Exists(Path.Combine(workTree, PlanPath)))
        {
            failures.Add($"The plan {PlanPath} is missing.");
        }

        foreach (string path in now.Keys.Union(baseline.Keys).Where(path => !IsFeature(path)).Order(StringComparer.Ordinal))
        {
            bool before = baseline.TryGetValue(path, out string? old);
            bool after = now.TryGetValue(path, out string? current);
            if (!before)
            {
                failures.Add($"The file {path} is new; leave every file outside {FeatureDirectory} as it is.");
            }
            else if (!after)
            {
                failures.Add($"The file {path} is gone; leave every file outside {FeatureDirectory} as it is.");
            }
            else if (old != current)
            {
                failures.Add($"The file {path} changed; leave every file outside {FeatureDirectory} as it is.");
            }
        }

        return failures;
    }

    private static bool IsFeature(string path) =>
        path.StartsWith(FeatureDirectory, StringComparison.Ordinal) && path.EndsWith(".feature", StringComparison.Ordinal);

    private static Dictionary<string, string> Snapshot(string workTree, bool all)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(workTree, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(workTree, file).Replace('\\', '/');
            string[] segments = relative.Split('/');
            if (segments[..^1].Any(segment => segment is ".git" or "bin" or "obj"))
            {
                continue;
            }

            bool inFeatures = relative.StartsWith(FeatureDirectory, StringComparison.Ordinal);
            if (relative.StartsWith(".factory/", StringComparison.Ordinal) || (!all && inFeatures && !IsFeature(relative)))
            {
                continue;
            }

            result[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return result;
    }
}
