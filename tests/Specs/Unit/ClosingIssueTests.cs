using System.Text.Json;
using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

/// <summary>The closing issue of a pull request, as <see cref="PullRequests.ReadClosingIssueAsync"/> reads it from GitHub's answer.</summary>
public sealed class ClosingIssueTests
{
    private static readonly IssueNumber Pull = new(new RepoName("sbiondolillo", "factory-scratch"), 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (PullRequests Sut, RecordingHandler Handler) Make(params (int Number, string Owner, string Name)[] issues)
    {
        var handler = new RecordingHandler();
        var nodes = issues.Select(i => new { number = i.Number, repository = new { name = i.Name, owner = new { login = i.Owner } } });
        handler.Answer(200, JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { closingIssuesReferences = new { nodes } } } } }));
        return (new PullRequests(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero)), handler);
    }

    [Fact]
    public async Task AnIssueOfTheSameRepoIsTheClosingIssue()
    {
        (PullRequests sut, RecordingHandler handler) = Make((11, "sbiondolillo", "factory-scratch"));
        Assert.Equal(new IssueNumber(Pull.Repo, 11), await sut.ReadClosingIssueAsync(Pull, Ct));
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal("POST https://api.github.com/graphql", $"{request.Method} {request.Uri}");
        using var document = JsonDocument.Parse(request.Content!);
        JsonElement root = document.RootElement;
        Assert.Contains("closingIssuesReferences", root.GetProperty("query").GetString(), StringComparison.Ordinal);
        JsonElement variables = root.GetProperty("variables");
        Assert.Equal("sbiondolillo", variables.GetProperty("owner").GetString());
        Assert.Equal("factory-scratch", variables.GetProperty("name").GetString());
        Assert.Equal(10, variables.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task NoIssueGivesNull()
    {
        (PullRequests sut, _) = Make();
        Assert.Null(await sut.ReadClosingIssueAsync(Pull, Ct));
    }

    [Theory]
    [InlineData("sbiondolillo", "homelab")]
    [InlineData("octocat", "factory-scratch")]
    public async Task AnIssueOfADifferentRepoIsSkippedForTheIssueOfTheSameRepo(string owner, string name)
    {
        (PullRequests sut, _) = Make((7, owner, name), (11, "sbiondolillo", "factory-scratch"));
        Assert.Equal(new IssueNumber(Pull.Repo, 11), await sut.ReadClosingIssueAsync(Pull, Ct));
    }

    [Fact]
    public async Task OnlyAnIssueOfADifferentRepoGivesNull()
    {
        (PullRequests sut, _) = Make((7, "octocat", "factory-scratch"));
        Assert.Null(await sut.ReadClosingIssueAsync(Pull, Ct));
    }

    [Fact]
    public async Task TheNamesOfTheRepoCompareWithoutCase()
    {
        (PullRequests sut, _) = Make((11, "SBiondolillo", "Factory-Scratch"));
        Assert.Equal(new IssueNumber(Pull.Repo, 11), await sut.ReadClosingIssueAsync(Pull, Ct));
    }

    [Fact]
    public async Task OfTwoIssuesOfTheSameRepoTheFirstIsTheClosingIssue()
    {
        (PullRequests sut, _) = Make((12, "sbiondolillo", "factory-scratch"), (11, "sbiondolillo", "factory-scratch"));
        Assert.Equal(new IssueNumber(Pull.Repo, 12), await sut.ReadClosingIssueAsync(Pull, Ct));
    }

    [Fact]
    public async Task AnAnswerWithErrorsThrowsGitHubGraphQLException()
    {
        (PullRequests sut, RecordingHandler handler) = Make();
        handler.Answer(200, "{\"data\":null,\"errors\":[{\"message\":\"Could not resolve to a PullRequest with the number of 10.\"}]}");
        GitHubGraphQLException ex = await Assert.ThrowsAsync<GitHubGraphQLException>(() => sut.ReadClosingIssueAsync(Pull, Ct));
        Assert.Equal(["Could not resolve to a PullRequest with the number of 10."], ex.Messages);
    }
}
