using System.Security.Cryptography;
using System.Text;

namespace Core;

/// <summary>The outcome of <see cref="PlanPhase"/>: passed with a pin, or failed with failure sentences.</summary>
public sealed record PlanPhaseResult(bool Passed, Pin? Pin, IReadOnlyList<string> Failures);

/// <summary>Asks the agent for feature files and a plan, checks the work tree, and pins the scenarios.</summary>
public sealed class PlanPhase(ClaudeCodeAgent agent)
{
    private const string FeatureDirectory = "specs/features/";
    private const string PlanPath = ".factory/plan.md";

    /// <summary>Runs the first prompt and, when its gate fails, one correction prompt.</summary>
    public async Task<PlanPhaseResult> RunAsync(
        string title,
        string body,
        string model,
        int maxTurns,
        string workTree,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> baseline = Snapshot(workTree);
        string prompt = BuildPrompt(title, body);

        List<string> failures = await RunOnceAsync(prompt, model, maxTurns, workTree, baseline, cancellationToken).ConfigureAwait(false);
        if (failures.Count is 0)
        {
            return new PlanPhaseResult(true, Pin.Take(workTree), []);
        }

        var correction = new StringBuilder(prompt)
            .Append("\n\nYour previous run failed these checks. Fix each one:\n");
        foreach (string failure in failures)
        {
            correction.Append("- ").Append(failure).Append('\n');
        }

        failures = await RunOnceAsync(correction.ToString(), model, maxTurns, workTree, baseline, cancellationToken).ConfigureAwait(false);
        return failures.Count is 0
            ? new PlanPhaseResult(true, Pin.Take(workTree), [])
            : new PlanPhaseResult(false, null, failures);
    }

    private static string BuildPrompt(string title, string body) =>
        "Plan the work of the issue below.\n\n"
        + $"Issue title:\n{title}\n\nIssue body:\n{body}\n\n"
        + $"Write Gherkin scenarios for the behaviour in feature files under {FeatureDirectory} "
        + $"and write the plan to {PlanPath}, stating each result the build owes. "
        + "Remove each feature file whose behaviour the issue removes or replaces. "
        + "Leave every other file as it is.";

    private async Task<List<string>> RunOnceAsync(
        string prompt,
        string model,
        int maxTurns,
        string workTree,
        Dictionary<string, string> baseline,
        CancellationToken cancellationToken)
    {
        ClaudeCodeResult answer = await agent.RunAsync(prompt, model, maxTurns, cancellationToken).ConfigureAwait(false);
        if (answer.IsError)
        {
            return [string.IsNullOrWhiteSpace(answer.Result) ? "claude answered with an error and no text." : answer.Result];
        }

        return Gate(workTree, baseline);
    }

    private static List<string> Gate(string workTree, Dictionary<string, string> baseline)
    {
        var failures = new List<string>();
        Dictionary<string, string> now = Snapshot(workTree);

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

    private static Dictionary<string, string> Snapshot(string workTree)
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
            if (relative.StartsWith(".factory/", StringComparison.Ordinal) || (inFeatures && !IsFeature(relative)))
            {
                continue;
            }

            result[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return result;
    }
}
