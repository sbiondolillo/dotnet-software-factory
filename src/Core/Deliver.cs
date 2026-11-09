using System.Globalization;
using System.Text;

namespace Core;

/// <summary>What the pull request body states about a run besides its issue: the base commit, the passed tests, the check gate, the Redos, the spend, the seconds, the factory version and the Managed Agents sessions.</summary>
public sealed record DeliveryFacts(
    string? BaseCommit = null,
    int PassedTests = 0,
    bool CheckGreen = false,
    int Redos = 0,
    decimal Spend = 0m,
    int Seconds = 0,
    string? Version = null,
    IReadOnlyList<SessionCost>? Sessions = null);

/// <summary>Delivers a run: pushes its branch, opens a pull request and links it from the issue.</summary>
public sealed class Deliver(Git git, PullRequests pullRequests, GitHubClient client, DeliveryFacts? facts = null)
{
    /// <summary>Pushes <c>factory/&lt;runId&gt;</c>, opens a pull request that closes the issue and comments its web address on the issue.</summary>
    public async Task<PullRequest> RunAsync(string workTree, FeatureFiles features, string runId, IssueNumber issue, string baseBranch, string issueTitle, string token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);
        string branch = "factory/" + runId;
        await git.CheckoutBranchAsync(branch, workTree, cancellationToken).ConfigureAwait(false);
        await git.PushAsync(branch, workTree, token, cancellationToken).ConfigureAwait(false);

        string body = await BuildBodyAsync(workTree, features, issue.Number, cancellationToken).ConfigureAwait(false);
        PullRequest pullRequest = await pullRequests.OpenAsync(issue.Repo, branch, baseBranch, issueTitle, body, cancellationToken).ConfigureAwait(false);
        await client.PostCommentAsync(issue, $"Opened {pullRequest.HtmlUrl}", cancellationToken).ConfigureAwait(false);
        return pullRequest;
    }

    private async Task<string> BuildBodyAsync(string workTree, FeatureFiles features, int issueNumber, CancellationToken cancellationToken)
    {
        DeliveryFacts known = facts ?? new DeliveryFacts();
        IReadOnlyList<string> scenarios = await ScenarioLinesAsync(known, workTree, features, cancellationToken).ConfigureAwait(false);
        var body = new StringBuilder();
        body.Append(CultureInfo.InvariantCulture, $"Closes #{issueNumber}\n\n## Scenarios\n\n");
        body.AppendJoin('\n', scenarios);
        body.Append(CultureInfo.InvariantCulture, $"\n\n## Gates\n\n- test: passed, {known.PassedTests} tests\n");
        if (known.CheckGreen)
        {
            body.Append("- check: green\n");
        }

        body.Append(CultureInfo.InvariantCulture, $"- review: approved\n\nRedos: {known.Redos}\n");
        if (known.BaseCommit is not null)
        {
            body.Append(CultureInfo.InvariantCulture, $"Base commit: {known.BaseCommit}\n");
        }

        body.Append('\n');
        if (known.Version is not null)
        {
            body.Append(CultureInfo.InvariantCulture, $"Factory version: {known.Version}\n");
        }

        body.Append(CultureInfo.InvariantCulture, $"Total: ${decimal.Round(known.Spend, 4, MidpointRounding.AwayFromZero)} in {known.Seconds} s");
        if (known.Sessions is { Count: > 0 } sessions)
        {
            body.Append("\nSessions: ").AppendJoin(", ", sessions.Select(session => $"{session.Id} ({session.Role}, {Dollars(session.Cents)})"));
        }

        return body.ToString();
    }

    /// <summary>The cents of a session as dollars with two decimal places and a leading dollar sign.</summary>
    internal static string Dollars(int cents) => string.Create(CultureInfo.InvariantCulture, $"${cents / 100m:0.00}");

    private async Task<IReadOnlyList<string>> ScenarioLinesAsync(DeliveryFacts known, string workTree, FeatureFiles features, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        if (known.BaseCommit is not null)
        {
            string[] changed = await git.ListChangedFilesAsync(known.BaseCommit, workTree, cancellationToken).ConfigureAwait(false);
            foreach (string path in changed.Where(p => FeatureFiles.IsFeaturePath(p) && p.EndsWith(".feature", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                string? before = await git.ReadFileAtAsync(known.BaseCommit, path, workTree, cancellationToken).ConfigureAwait(false);
                string line = Describe(path, Read(path, before), ReadAfter(features, path));
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }

        if (lines.Count is 0)
        {
            lines.Add("- No scenario changed.");
        }

        return lines;
    }

    /// <summary>The scenarios of the file after the build, or none when no file is there or it cannot be read.</summary>
    private static IReadOnlyList<(string Title, string Steps)> ReadAfter(FeatureFiles features, string path)
    {
        try
        {
            return features.ScenarioSteps(path);
        }
        catch (FeatureFileException)
        {
            return [];
        }
    }

    private static IReadOnlyList<(string Title, string Steps)> Read(string path, string? content)
    {
        if (content is null)
        {
            return [];
        }

        try
        {
            return FeatureFiles.ReadScenarioSteps(path, content);
        }
        catch (FeatureFileException)
        {
            return [];
        }
    }

    private static string Describe(string path, IReadOnlyList<(string Title, string Steps)> before, IReadOnlyList<(string Title, string Steps)> after)
    {
        Dictionary<string, string> was = [];
        foreach ((string title, string steps) in before)
        {
            was[title] = steps;
        }

        Dictionary<string, string> now = [];
        foreach ((string title, string steps) in after)
        {
            now[title] = steps;
        }

        List<string> kinds = [];
        AddKind(kinds, "added", now.Keys.Where(t => !was.ContainsKey(t)));
        AddKind(kinds, "changed", now.Keys.Where(t => was.TryGetValue(t, out string? old) && old != now[t]));
        AddKind(kinds, "removed", was.Keys.Where(t => !now.ContainsKey(t)));
        return kinds.Count is 0 ? "" : $"- `{path}`: {string.Join("; ", kinds)}";
    }

    private static void AddKind(List<string> kinds, string kind, IEnumerable<string> titles)
    {
        string[] quoted = [.. titles.Select(t => $"\"{t}\"")];
        if (quoted.Length > 0)
        {
            kinds.Add($"{kind} {string.Join(", ", quoted)}");
        }
    }
}
