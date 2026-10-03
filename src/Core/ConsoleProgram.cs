using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core;

/// <summary>The console program: the verbs <c>run</c> and <c>feedback</c>, over a <see cref="ConsoleHost"/>.</summary>
public static class ConsoleProgram
{
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
        var root = new RootCommand("Runs the software factory on an issue, or on the feedback of a pull request.");
        root.Subcommands.Add(Verb("run", "Builds a pull request for an issue.", "--issue", "The number of the issue.", host));
        root.Subcommands.Add(Verb("feedback", "Answers the review threads of a pull request.", "--pr", "The number of the pull request.", host));
        var configuration = new InvocationConfiguration { Output = host.Out, Error = host.Error };
        return await root.Parse(args).InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    private static Command Verb(string verb, string description, string numberName, string numberDescription, ConsoleHost host)
    {
        var repo = new Option<string>("--repo") { Description = "The repo, as <owner>/<name>.", Required = true };
        repo.Validators.Add(result =>
        {
            if (result.Tokens.Count is 0)
            {
                return;
            }

            string[] parts = result.Tokens[0].Value.Split('/');
            if (parts is not [{ Length: > 0 }, { Length: > 0 }])
            {
                result.AddError("Option '--repo' must have the form <owner>/<name>.");
            }
        });
        var number = new Option<int>(numberName) { Description = numberDescription, Required = true };
        number.Validators.Add(result =>
        {
            if (Value(result) < 1)
            {
                result.AddError($"Option '{numberName}' must be 1 or more.");
            }
        });
        var passEnv = new Option<string[]>("--pass-env")
        {
            Description = "The name of a variable to give to the integration tests. May repeat.",
            AllowMultipleArgumentsPerToken = false,
        };
        var timeout = new Option<int>("--timeout")
        {
            Description = "The minutes each watch waits.",
            DefaultValueFactory = _ => 60,
        };
        timeout.Validators.Add(result =>
        {
            if (Value(result) < 0)
            {
                result.AddError("Option '--timeout' must be 0 or more.");
            }
        });

        var command = new Command(verb, description) { repo, number, passEnv, timeout };
        command.SetAction(async (parse, token) =>
        {
            string[] names = parse.GetValue(passEnv) ?? [];
            string[] repoParts = parse.GetValue(repo)!.Split('/');
            var options = new Options(verb, new RepoName(repoParts[0], repoParts[1]), parse.GetValue(number), names, TimeSpan.FromMinutes(parse.GetValue(timeout)));
            return await RunVerbAsync(options, host, token).ConfigureAwait(false);
        });
        return command;
    }

    /// <summary>The whole number the option's token holds, or <see cref="int.MaxValue"/> when it holds none, for the parser's own line to report.</summary>
    private static int Value(OptionResult result) =>
        result.Tokens is [{ } token] && int.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : int.MaxValue;

    private static async Task<int> RunVerbAsync(Options options, ConsoleHost host, CancellationToken cancellationToken)
    {
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

    private sealed record RepoName(string Owner, string Name)
    {
        public void Deconstruct(out string owner, out string name) => (owner, name) = (Owner, Name);
    }

    private sealed record Options(string Verb, RepoName Repo, int Number, IReadOnlyList<string> SecretNames, TimeSpan Timeout);
}
