using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Core;

/// <summary>The command tree of the console program: the verbs <c>run</c> and <c>feedback</c>, over a <see cref="ConsoleHost"/>.</summary>
public static partial class ConsoleCommands
{
    private const string TokenName = "FACTORY_GITHUB_TOKEN";
    private const string ChecksTokenName = "FACTORY_CHECKS_TOKEN";
    private const string KeyName = "ANTHROPIC_API_KEY";
    private const string AgentIdName = "FACTORY_AGENT_ID";
    private const string EnvironmentIdName = "FACTORY_ENVIRONMENT_ID";
    private const string AppIdName = "FACTORY_APP_ID";
    private const string InstallationIdName = "FACTORY_APP_INSTALLATION_ID";
    private const string PrivateKeyName = "FACTORY_APP_PRIVATE_KEY";
    private const string ImageTag = "dotnet-software-factory-sandbox";

    /// <summary>The command tree the console program parses and invokes.</summary>
    public static RootCommand Create(ConsoleHost host)
    {
        var root = new RootCommand("Runs the software factory on an issue, or on the feedback of a pull request.");
        root.Subcommands.Add(Verb("run", "Builds a pull request for an issue.", "--issue", "The number of the issue.", host));
        root.Subcommands.Add(Verb("feedback", "Answers the review threads of a pull request.", "--pr", "The number of the pull request.", host));
        return root;
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

        var copilotReview = new Option<string>("--copilot-review")
        {
            Description = "Whether the factory requests a Copilot code review and waits for it: on or off.",
            DefaultValueFactory = _ => "off",
        };
        copilotReview.Validators.Add(result =>
        {
            if (result.Tokens.Count is not 0 && result.Tokens is not [{ Value: "on" or "off" }])
            {
                result.AddError("Option '--copilot-review' must be on or off.");
            }
        });

        var skipIntegration = new Option<string>("--skip-integration")
        {
            Description = "Whether the test gate skips the integration suite: on or off.",
            DefaultValueFactory = _ => "off",
        };
        skipIntegration.Validators.Add(result =>
        {
            if (result.Tokens.Count is not 0 && result.Tokens is not [{ Value: "on" or "off" }])
            {
                result.AddError("Option '--skip-integration' must be on or off.");
            }
        });

        var agent = new Option<string>("--agent")
        {
            Description = "claude-code or managed",
            DefaultValueFactory = _ => "claude-code",
        };
        agent.Validators.Add(result =>
        {
            if (result.Tokens.Count is not 0 && result.Tokens is not [{ Value: "claude-code" or "managed" }])
            {
                result.AddError("Option '--agent' must be claude-code or managed.");
            }
        });

        var command = new Command(verb, description) { repo, number, passEnv, timeout, copilotReview, skipIntegration, agent };
        var rounds = new Option<int>("--review-rounds")
        {
            Description = "The most feedback runs one run makes.",
            DefaultValueFactory = _ => 3,
        };
        rounds.Validators.Add(result =>
        {
            if (Value(result) < 0)
            {
                result.AddError("Option '--review-rounds' must be 0 or more.");
            }
        });
        var baseCommit = new Option<string>("--base")
        {
            Description = "The full SHA of the commit the run builds on, in place of the head of the default branch.",
        };
        baseCommit.Validators.Add(result =>
        {
            if (result.Tokens is [{ } token] && !FullSha().IsMatch(token.Value))
            {
                result.AddError("Option '--base' must be the full SHA of a commit: 40 lowercase hexadecimal characters.");
            }
        });
        if (verb is "run")
        {
            command.Options.Add(rounds);
            command.Options.Add(baseCommit);
        }

        command.SetAction(async (parse, token) =>
        {
            string[] names = parse.GetValue(passEnv) ?? [];
            string[] repoParts = parse.GetValue(repo)!.Split('/');
            var options = new Options(verb, new RepoName(repoParts[0], repoParts[1]), parse.GetValue(number), names, TimeSpan.FromMinutes(parse.GetValue(timeout)), verb is "run" ? parse.GetValue(rounds) : 0, parse.GetValue(copilotReview) is not "off", parse.GetValue(skipIntegration) is not "off", parse.GetValue(agent) is "managed")
            {
                BaseCommit = verb is "run" ? parse.GetValue(baseCommit) : null,
            };
            return await RunVerbAsync(options, host, token).ConfigureAwait(false);
        });
        return command;
    }

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullSha();

    /// <summary>The whole number the option's token holds, or <see cref="int.MaxValue"/> when it holds none, for the parser's own line to report.</summary>
    private static int Value(OptionResult result) =>
        result.Tokens is [{ } token] && int.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : int.MaxValue;

    private static async Task<int> RunVerbAsync(Options options, ConsoleHost host, CancellationToken cancellationToken)
    {
        string[] required = options.Managed ? [TokenName, KeyName, AgentIdName, EnvironmentIdName, AppIdName, InstallationIdName, PrivateKeyName] : [TokenName, KeyName];
        foreach (string name in required)
        {
            if (string.IsNullOrEmpty(host.Environment(name)))
            {
                await host.Error.WriteLineAsync($"The environment variable {name} is not set.").ConfigureAwait(false);
                return 1;
            }
        }

        if (options.Managed)
        {
            options = options with { Settings = ReadSettings(host) };
        }

        string token = host.Environment(TokenName)!;
        using var http = new HttpClient(host.Handler, disposeHandler: false);
        var client = new GitHubClient(http, token);
        string checksToken = host.Environment(ChecksTokenName) is { Length: > 0 } own ? own : token;
        GitHubClient checksClient = checksToken == token ? client : new GitHubClient(http, checksToken);
        try
        {
            return await ExecuteAsync(options, host, client, checksClient, token, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubRequestException ex)
        {
            await host.Error.WriteLineAsync($"GitHub refused a request with the status code {ex.StatusCode.ToString(CultureInfo.InvariantCulture)}.").ConfigureAwait(false);
            return 1;
        }
        catch (GitHubGraphQLException ex)
        {
            await host.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(Options options, ConsoleHost host, GitHubClient client, GitHubClient checksClient, string token, CancellationToken cancellationToken)
    {
        SettleSettings settings = host.Settings;
        StartBackend backend = host.Backend(new BackendOptions(
            host.Runner, ImageTag, host.RootDirectory, options.Settings, options.Repo.Owner, options.Repo.Name, token, settings.AuthorName, settings.AuthorEmail, host.Handler, host.Delay, host.Error, host.Time, host.Out));
        var settle = new Settle(host.Runner, client, checksClient, token, backend, settings, host.Out, host.Error, host.Time, host.Delay, host.Environment("GITHUB_STEP_SUMMARY"));
        var settleOptions = new SettleOptions(options.ReviewRounds, options.CopilotReview, options.Timeout, options.SecretNames, options.SkipIntegration);
        var number = new IssueNumber(options.Repo, options.Number);
        SettleResult result = options.Verb is "run"
            ? await settle.SettleIssueAsync(number, options.BaseCommit, settleOptions, cancellationToken).ConfigureAwait(false)
            : await settle.SettlePullRequestAsync(number, settleOptions, cancellationToken).ConfigureAwait(false);
        return result.End is SettleEnd.Settled or SettleEnd.Declined ? 0 : 1;
    }

    /// <summary>The settings of the managed path, from the variables that <see cref="RunVerbAsync"/> found set.</summary>
    private static ManagedAgentsSettings ReadSettings(ConsoleHost host) => new(
        host.Environment(KeyName)!,
        host.Environment(AgentIdName)!,
        host.Environment(EnvironmentIdName)!,
        host.Environment(AppIdName)!,
        host.Environment(InstallationIdName)!,
        host.Environment(PrivateKeyName)!);

    private sealed record Options(string Verb, RepoName Repo, int Number, IReadOnlyList<string> SecretNames, TimeSpan Timeout, int ReviewRounds, bool CopilotReview, bool SkipIntegration, bool Managed)
    {
        /// <summary>The settings of the managed path, or null for the path of Claude Code in a sandbox.</summary>
        public ManagedAgentsSettings? Settings { get; init; }

        /// <summary>The full SHA of the commit a run builds on, or null for the head of the default branch.</summary>
        public string? BaseCommit { get; init; }
    }
}
