using Core;
using Moq;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class DeliverTests
{
    private static (Deliver Deliver, List<string> Git, RoutingHandler Handler) Build(string failing = "", int status = 200)
    {
        var git = new List<string>();
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                git.Add(spec.Arguments[0]);
                return Task.FromResult(new CommandResult { ExitCode = spec.Arguments[0] == failing ? 1 : 0, StandardError = "fatal" });
            });
        var handler = new RoutingHandler { PostStatus = status, PostBody = "{\"number\":10,\"html_url\":\"https://x/pull/10\"}" };
        var client = new GitHubClient(new HttpClient(handler), "token", TimeSpan.Zero);
        return (new Deliver(new Git(mock.Object), new PullRequests(client), client), git, handler);
    }

    private static Task<PullRequest> RunAsync(Deliver deliver) =>
        deliver.RunAsync("/work", "abc", "o", "r", "main", 7, "Title", "token", TestContext.Current.CancellationToken);

    private static string PullBody(RoutingHandler handler) =>
        System.Text.Json.JsonDocument.Parse(handler.Requests.Single(r => r.Uri.EndsWith("/pulls", StringComparison.Ordinal) && r.Method is "POST").Content!).RootElement.GetProperty("body").GetString()!;

    private static async Task<string> BodyAsync(Dictionary<string, string> baseFiles, Dictionary<string, string> workFiles, DeliveryFacts? facts = null)
    {
        DirectoryInfo tree = Directory.CreateTempSubdirectory("deliver-tree-");
        try
        {
            foreach ((string path, string text) in workFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(tree.FullName, path))!);
                File.WriteAllText(Path.Combine(tree.FullName, path), text);
            }

            var mock = new Mock<CommandRunner>(MockBehavior.Strict);
            mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
                .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(spec.Arguments[0] switch
                {
                    "diff" => new CommandResult { StandardOutput = string.Join('\n', baseFiles.Keys.Concat(workFiles.Keys).Distinct()) },
                    "show" when baseFiles.TryGetValue(spec.Arguments[1][(spec.Arguments[1].IndexOf(':', StringComparison.Ordinal) + 1)..], out string? text) => new CommandResult { StandardOutput = text },
                    "show" => new CommandResult { ExitCode = 128, StandardError = "fatal: path does not exist" },
                    _ => new CommandResult(),
                }));
            var handler = new RoutingHandler { PostStatus = 200, PostBody = "{\"number\":10,\"html_url\":\"https://x/pull/10\"}" };
            var client = new GitHubClient(new HttpClient(handler), "token", TimeSpan.Zero);
            var deliver = new Deliver(new Git(mock.Object), new PullRequests(client), client, facts ?? new DeliveryFacts("base"));
            await deliver.RunAsync(tree.FullName, "abc", "o", "r", "main", 7, "Title", "token", TestContext.Current.CancellationToken);
            return PullBody(handler);
        }
        finally
        {
            tree.Delete(recursive: true);
        }
    }

    private const string Path1 = "specs/features/a.feature";
    private const string Feature = "Feature: F\n  Scenario: One\n    Given a step\n";

    [Fact]
    public async Task AFileTheBaseLacksGivesEachScenarioAdded()
    {
        string body = await BodyAsync([], new() { [Path1] = Feature + "  Scenario: Two\n    Given b\n" });
        Assert.Contains("- `specs/features/a.feature`: added \"One\", \"Two\"\n", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileTheWorkTreeLacksGivesEachScenarioRemoved()
    {
        string body = await BodyAsync(new() { [Path1] = Feature }, []);
        Assert.Contains("- `specs/features/a.feature`: removed \"One\"\n", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameStepsOnBothSidesGiveNoLine()
    {
        string body = await BodyAsync(new() { [Path1] = Feature }, new() { [Path1] = Feature });
        Assert.Contains("## Scenarios\n\n- No scenario changed.\n\n## Gates", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunWithNoVersionStatesNoVersionAndEndsWithTheTotal()
    {
        string body = await BodyAsync([], [], new DeliveryFacts("base", 3, false, 2, 0.7500m, 31));
        Assert.DoesNotContain("Factory version", body, StringComparison.Ordinal);
        Assert.DoesNotContain("check:", body, StringComparison.Ordinal);
        Assert.EndsWith("\nRedos: 2\nBase commit: base\n\nTotal: $0.7500 in 31 s", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTotalRoundsTheSpendToFourDecimalPlaces()
    {
        string body = await BodyAsync([], [], new DeliveryFacts("base", 3, false, 2, 0.21645510000000004m, 209));
        Assert.EndsWith("Total: $0.2165 in 209 s", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVersionComesBeforeTheTotal()
    {
        string body = await BodyAsync([], [], new DeliveryFacts("base", Version: "1.0.0+abc"));
        Assert.EndsWith("\n\nFactory version: 1.0.0+abc\nTotal: $0 in 0 s", body, StringComparison.Ordinal);
    }

    private static async Task<string> RunBodyAsync(Action<JobSummaryHarness> setup)
    {
        using var harness = new JobSummaryHarness();
        setup(harness);
        await harness.RunAsync(TestContext.Current.CancellationToken);
        return harness.PullBody!;
    }

    [Fact]
    public async Task APartOfASecondIsDroppedAndTheScaleOfTheCostsStays()
    {
        string body = await RunBodyAsync(h =>
        {
            h.PlanAnswers.Add(new SummaryAnswer(1, "0.5000", 10.9));
            h.ReviewAnswers[1] = new SummaryAnswer(1, "0.2500", 20.9);
        });
        Assert.EndsWith("Total: $0.7500 in 31 s", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Factory version", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunWithNeitherLabelStatesNoCheckGate()
    {
        string body = await RunBodyAsync(_ => { });
        Assert.DoesNotContain("check:", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefactorRunWithNoCheckSectionStatesNoCheckGate()
    {
        string body = await RunBodyAsync(h =>
        {
            h.Labels.Add("refactor");
            h.Body = "Move the checkout total into its own class.";
        });
        Assert.DoesNotContain("check:", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APartOfASecondIsDroppedAndTheScaleOfTheCostsStaysWithAVersion()
    {
        string body = await RunBodyAsync(h => h.Version = "9.9");
        Assert.Contains("\n\nFactory version: 9.9\nTotal: $0 in 0 s", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APushThatFailsThrowsAndSendsNoRequest()
    {
        (Deliver? deliver, List<string> _, RoutingHandler? handler) = Build("push");
        await Assert.ThrowsAsync<GitCommandException>(() => RunAsync(deliver));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ACheckoutThatFailsThrowsAndRunsNoPushAndSendsNoRequest()
    {
        (Deliver? deliver, List<string>? git, RoutingHandler? handler) = Build("checkout");
        await Assert.ThrowsAsync<GitCommandException>(() => RunAsync(deliver));
        Assert.Equal(["checkout"], git);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A422OnTheOpenThrowsAndPostsNoComment()
    {
        (Deliver? deliver, List<string> _, RoutingHandler? handler) = Build(status: 422);
        GitHubRequestException exception = await Assert.ThrowsAsync<GitHubRequestException>(() => RunAsync(deliver));
        Assert.Equal(422, exception.StatusCode);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.EndsWith("/comments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheHappyPathOpensOnePullRequestAndPostsOneComment()
    {
        (Deliver? deliver, List<string>? git, RoutingHandler? handler) = Build();
        PullRequest result = await RunAsync(deliver);
        Assert.Equal(new PullRequest(10, "https://x/pull/10"), result);
        Assert.Equal(["checkout", "push"], git);
        string body = PullBody(handler);
        Assert.StartsWith("Closes #7", body, StringComparison.Ordinal);
        Assert.Matches(@"\nTotal: \$0 in 0 s$", body);
        Assert.Contains("https://x/pull/10", Assert.Single(handler.Requests, r => r.Uri.EndsWith("/comments", StringComparison.Ordinal)).Content, StringComparison.Ordinal);
    }
}
