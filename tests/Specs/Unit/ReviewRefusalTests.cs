using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class ReviewRefusalTests
{
    private const string Reviewer = "copilot-pull-request-reviewer";
    private const string Sha = "abc";
    private const string Text = "Copilot was unable to review this pull request because the user who requested the review has reached their quota limit.";

    [Fact]
    public async Task TheBodyOfAReviewIsRead()
    {
        var handler = new RecordingHandler();
        handler.Answer(200, WatchHandler.ReviewsWithBodiesJson([(Reviewer, "COMMENTED", Sha, Text)]));
        var sut = new ReviewThreads(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero));
        Review review = Assert.Single(await sut.ReadReviewsAsync("o", "r", 1, TestContext.Current.CancellationToken));
        Assert.Equal(Text, review.Body);
    }

    [Fact]
    public async Task AReviewWithNoBodyReadsAnEmptyBody()
    {
        var handler = new RecordingHandler();
        handler.Answer(200, "{\"data\":{\"repository\":{\"pullRequest\":{\"reviews\":{\"nodes\":[{\"state\":\"COMMENTED\"}]}}}}}");
        var sut = new ReviewThreads(new GitHubClient(new HttpClient(handler), "t", TimeSpan.Zero));
        Review review = Assert.Single(await sut.ReadReviewsAsync("o", "r", 1, TestContext.Current.CancellationToken));
        Assert.Equal("", review.Body);
    }

    [Fact]
    public void ARefusalByTheReviewerOnTheCommitGivesItsBody() =>
        Assert.Equal(Text, Review.Refusal([new Review(Reviewer, "COMMENTED", Sha, Text)], Reviewer, Sha));

    [Theory]
    [InlineData(Reviewer, "COMMENTED", Sha, "Looks fine.")]
    [InlineData("someone", "COMMENTED", Sha, Text)]
    [InlineData(Reviewer, "COMMENTED", "other", Text)]
    [InlineData(Reviewer, "APPROVED", Sha, Text)]
    [InlineData(Reviewer, "COMMENTED", "", Text)]
    public void AnyOtherReviewGivesNoRefusal(string author, string state, string commit, string body) =>
        Assert.Null(Review.Refusal([new Review(author, state, commit, body)], Reviewer, Sha));
}
