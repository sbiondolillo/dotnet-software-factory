using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core;

/// <summary>The console program: the verbs <c>run</c> and <c>feedback</c>, over a <see cref="ConsoleHost"/>.</summary>
public static class ConsoleProgram
{
    /// <summary>The usage of each verb.</summary>
    public const string Usage =
        "usage:\n" +
        "  dotnet-software-factory run --repo <owner>/<name> --issue <number> [--pass-env <NAME>]... [--timeout <minutes>]\n" +
        "  dotnet-software-factory feedback --repo <owner>/<name> --pr <number> [--pass-env <NAME>]... [--timeout <minutes>]\n" +
        "--pass-env gives the variable NAME to the integration tests, and may repeat. --timeout is the minutes each watch waits, 60 when absent.\n";

    private const string TokenName = "FACTORY_GITHUB_TOKEN";
    private const string KeyName = "ANTHROPIC_API_KEY";
    private const string Model = "claude-sonnet-5-5";
    private const string AuthorName = "dotnet-software-factory";
    private const string AuthorEmail = "dotnet-software-factory@noreply.github.com";
    private const string ImageTag = "dotnet-software-factory-sandbox";
    private const string Reviewer = "copilot-pull-request-reviewer";
    private static readonly TimeSpan PollWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeadGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeadPoll = TimeSpan.FromSeconds(1);

    /// <summary>Runs the program with <paramref name="args"/> and gives back its exit code.</summary>
    public static async Task<int> RunAsync(string[] args, ConsoleHost host, CancellationToken cancellationToken = default)
    {
        if (args.Contains("--help"))
        {
            await host.Out.WriteAsync(Usage).ConfigureAwait(false);
            return 0;
        }

        Options? options = Parse(args);
        if (options is null)
        {
            await host.Error.WriteAsync(Usage).ConfigureAwait(false);
            return 1;
        }

        foreach (string name in new[] { TokenName, KeyName })
        {
            if (string.IsNullOrEmpty(host.Environment(name)))
            {
                await host.Error.WriteLineAsync($"The environment variable {name} is not set.").ConfigureAwait(false);
                return 1;
            }
        }

        string token = host.Environment(TokenName)!;
        using var http = new HttpClient(host.Handler, disposeHandler: false);
        var client = new GitHubClient(http, token);
        try
        {
            return await ExecuteAsync(options, host, client, token, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubRequestException ex)
        {
            await host.Error.WriteLineAsync($"GitHub refused a request with the status code {ex.StatusCode.ToString(CultureInfo.InvariantCulture)}.").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(Options options, ConsoleHost host, GitHubClient client, string token, CancellationToken cancellationToken)
    {
        var pullRequests = new PullRequests(client);
        var reviewThreads = new ReviewThreads(client);
        var watch = new PullRequestWatch(pullRequests, reviewThreads, host.Delay, host.Time);
        var git = new Git(host.Runner);
        (string owner, string repo) = options.Repo;

        JsonElement repoJson = await client.SendAsync(HttpMethod.Get, $"/repos/{owner}/{repo}", null, cancellationToken).ConfigureAwait(false);
        string baseBranch = repoJson.GetProperty("default_branch").GetString() ?? "";

        int number;
        string? localHead;
        if (options.Verb is "run")
        {
            string runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-run-");
            FactoryRunResult result;
            try
            {
                var request = new FactoryRunRequest(
                    owner, repo, options.Number, token, runId, directory.FullName, baseBranch, ImageTag, host.RootDirectory,
                    Model, 50, Model, 200, Model, 50, AuthorName, AuthorEmail, options.SecretNames);
                result = await new FactoryRun(host.Runner, client).RunAsync(request, cancellationToken).ConfigureAwait(false);
                localHead = result.PullRequest is null ? null : await git.ReadHeadShaAsync(directory.FullName, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Remove(directory);
            }

            if (result.PullRequest is not PullRequest pullRequest)
            {
                return 1;
            }

            number = pullRequest.Number;
            await host.Out.WriteLineAsync(pullRequest.HtmlUrl).ConfigureAwait(false);
        }
        else
        {
            number = options.Number;
            localHead = null;
        }

        if (options.Verb is "feedback")
        {
            localHead = await FeedbackAsync(options, host, client, token, number, cancellationToken).ConfigureAwait(false);
        }

        PullRequestWatchResult last = await WatchAsync(host, pullRequests, watch, owner, repo, number, localHead, options.Timeout, cancellationToken).ConfigureAwait(false);
        if (options.Verb is "run" && last.Threads.Count > 0)
        {
            localHead = await FeedbackAsync(options, host, client, token, number, cancellationToken).ConfigureAwait(false);
            last = await WatchAsync(host, pullRequests, watch, owner, repo, number, localHead, options.Timeout, cancellationToken).ConfigureAwait(false);
        }

        await host.Out.WriteLineAsync($"checks: {(last.IsGreen ? "green" : "red")}, unresolved threads: {last.Threads.Count.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        return last.IsGreen && last.Threads.Count is 0 ? 0 : 1;
    }

    /// <summary>Runs one feedback run in a new work tree, and gives back the head commit of that work tree, or null when it cloned nothing.</summary>
    private static async Task<string?> FeedbackAsync(Options options, ConsoleHost host, GitHubClient client, string token, int number, CancellationToken cancellationToken)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-feedback-");
        try
        {
            var request = new FeedbackRunRequest(
                options.Repo.Owner, options.Repo.Name, number, token, directory.FullName, ImageTag, host.RootDirectory,
                Model, 200, Model, 50, AuthorName, AuthorEmail, options.SecretNames);
            FeedbackRunResult result = await new FeedbackRun(host.Runner, client).RunAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Resolved + result.Unresolved is 0
                ? null
                : await new Git(host.Runner).ReadHeadShaAsync(directory.FullName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Remove(directory);
        }
    }

    private static async Task<PullRequestWatchResult> WatchAsync(
        ConsoleHost host,
        PullRequests pullRequests,
        PullRequestWatch watch,
        string owner,
        string repo,
        int number,
        string? localHead,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (localHead is not null)
        {
            long? firstDifferent = null;
            while (true)
            {
                string sha = await pullRequests.ReadHeadShaAsync(owner, repo, number, cancellationToken).ConfigureAwait(false);
                if (sha == localHead)
                {
                    break;
                }

                firstDifferent ??= host.Time.GetTimestamp();
                if (host.Time.GetElapsedTime(firstDifferent.Value) >= HeadGrace)
                {
                    break;
                }

                await host.Delay(HeadPoll, cancellationToken).ConfigureAwait(false);
            }
        }

        return await watch.WatchAsync(owner, repo, number, Reviewer, PollWait, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static void Remove(DirectoryInfo directory)
    {
        if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }

    private static Options? Parse(string[] args)
    {
        if (args.Length is 0 || args[0] is not ("run" or "feedback"))
        {
            return null;
        }

        string verb = args[0];
        string numberOption = verb is "run" ? "--issue" : "--pr";
        string? repoText = null;
        int? number = null;
        int minutes = 60;
        List<string> names = [];
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                return null;
            }

            string value = args[i + 1];
            if (args[i] is "--repo")
            {
                repoText = value;
            }
            else if (args[i] == numberOption)
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
                {
                    return null;
                }

                number = parsed;
            }
            else if (args[i] is "--pass-env")
            {
                names.Add(value);
            }
            else if (args[i] is "--timeout")
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        string[]? parts = repoText?.Split('/');
        if (parts is not [{ Length: > 0 } owner, { Length: > 0 } name] || number is not int issue)
        {
            return null;
        }

        return new Options(verb, new RepoName(owner, name), issue, names, TimeSpan.FromMinutes(minutes));
    }

    private sealed record RepoName(string Owner, string Name)
    {
        public void Deconstruct(out string owner, out string name) => (owner, name) = (Owner, Name);
    }

    private sealed record Options(string Verb, RepoName Repo, int Number, IReadOnlyList<string> SecretNames, TimeSpan Timeout);
}
