using System.Text.Json;
using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class PullRequestHeadBranchSteps(RecordingHandler handler)
{
    private string? _branch;
    private Exception? _exception;

    [Given("GitHub answers the read of pull request {int} of {string} with the head ref {string}")]
    public void GivenAnswer(int number, string slug, string headRef) =>
        handler.Answer(200, JsonSerializer.Serialize(new { number, head = new { @ref = headRef, sha = "abc" } }));

    [Given("GitHub fails the read of pull request {int} of {string}")]
    public void GivenFails(int number, string slug) => handler.Answer(404, "{\"message\":\"Not Found\"}");

    [When("I read the head branch of pull request {int} of {string}")]
    public async Task WhenRead(int number, string slug)
    {
        string[] parts = slug.Split('/');
        var pullRequests = new PullRequests(new GitHubClient(new HttpClient(handler), "token", TimeSpan.Zero));
        try
        {
            _branch = await pullRequests.ReadHeadBranchAsync(parts[0], parts[1], number, TestContext.Current.CancellationToken);
        }
        catch (GitHubRequestException ex)
        {
            _exception = ex;
        }
    }

    [Then("the head branch is {string}")]
    public void ThenBranch(string expected) => Assert.Equal(expected, _branch);

    [Then("the read throws the GitHubRequestException")]
    public void ThenThrows() => Assert.IsType<GitHubRequestException>(_exception);
}
