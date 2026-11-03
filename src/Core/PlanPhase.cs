using System.Security.Cryptography;
using System.Text;

namespace Core;

/// <summary>The outcome of <see cref="PlanPhase"/>: passed with a pin, or failed with failure sentences.</summary>
public sealed record PlanPhaseResult(bool Passed, Pin? Pin, IReadOnlyList<string> Failures);

/// <summary>The gate of <see cref="PlanPhase"/>: feature files and a plan, or the plan alone.</summary>
public enum PlanMode
{
    FeaturesAndPlan,
    PlanOnly,
}

/// <summary>The words of the plan prompt: the lead before the issue and the instructions after it. <paramref name="Mode"/> is the
/// gate that checks the work tree after the prompt.</summary>
public sealed record PlanText(string Lead, string Instructions, PlanMode Mode);

/// <summary>Asks the agent for the work that <paramref name="text"/> states, checks the work tree, and pins the scenarios.
/// With <see cref="PlanMode.PlanOnly"/>, the agent writes the plan alone.</summary>
public sealed class PlanPhase(Agent agent, PlanText text)
{
    private bool PlanOnly => text.Mode is PlanMode.PlanOnly;

    private const string FeatureDirectory = "specs/features/";
    private const string PlanName = "plan.md";
    private const string PlanPath = ".factory/" + PlanName;

    /// <summary>The sentence that ends the correction prompt. The agent removes each output before a prompt, so the plan of the
    /// first run is gone.</summary>
    public const string CorrectionPlanSentence = "\nWrite the whole plan to " + PlanPath + " again in this run. The host removes the plan of the run before.\n";

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

        correction.Append(CorrectionPlanSentence);

        (failures, _) = await RunOnceAsync(correction.ToString(), model, maxTurns, workTree, baseline, cancellationToken).ConfigureAwait(false);
        return failures.Count is 0
            ? new PlanPhaseResult(true, Pin.Take(workTree), [])
            : new PlanPhaseResult(false, null, failures);
    }

    private string BuildPrompt(string title, string body) =>
        text.Lead + "\n\n"
        + $"Issue title:\n{title}\n\nIssue body:\n{body}\n\n"
        + text.Instructions;

    private async Task<(List<string> Failures, bool IsError)> RunOnceAsync(
        string prompt,
        string model,
        int maxTurns,
        string workTree,
        Dictionary<string, string> baseline,
        CancellationToken cancellationToken)
    {
        AgentAnswer answer = await agent.RunAsync(new AgentRequest(prompt, model, maxTurns, Phase.Plan, [PlanName]), cancellationToken).ConfigureAwait(false);
        if (answer.IsError)
        {
            return ([answer.Result], true);
        }

        bool planned = answer.Outputs.ContainsKey(PlanName);
        return (PlanOnly ? RefactorGate(workTree, baseline, planned) : Gate(workTree, baseline, planned), false);
    }

    private static List<string> RefactorGate(string workTree, Dictionary<string, string> baseline, bool planned)
    {
        var failures = new List<string>();
        Dictionary<string, string> now = Snapshot(workTree, all: true);
        if (!planned)
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

    private static List<string> Gate(string workTree, Dictionary<string, string> baseline, bool planned)
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

                bool unchanged = baseline.TryGetValue(path, out string? before) && before == now[path];
                foreach (int line in unchanged ? [] : FeatureFiles.ReadOverIndentedDocStringLines(path, File.ReadAllText(Path.Combine(workTree, path))))
                {
                    failures.Add($"The feature file {path}: the doc string at line {line} has text indented past its opening delimiter; start the text at the column of its opening delimiter.");
                }
            }
            catch (FeatureFileException exception)
            {
                failures.Add($"The feature file {path} does not parse: {exception.Message}");
            }
        }

        if (!planned)
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
