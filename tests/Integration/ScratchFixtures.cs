namespace Integration;

/// <summary>
/// The fixed fixtures in sbiondolillo/factory-scratch, which no test changes. A test that reads a
/// fixture creates no content, so a run sends fewer writes toward GitHub's limit for content creation.
/// </summary>
/// <remarks>
/// To make them again, as the owner of factory-scratch, then put the new numbers here:
/// <list type="number">
/// <item>The branch <c>fixture/empty</c> from <c>main</c>, with one new file <c>fixture/empty.txt</c>, and a pull
/// request from it to <c>main</c>. It touches no path under <c>watch-fixture/</c>, so it gets no check run. It gets no review.</item>
/// <item>The branch <c>watch-green/fixture</c> from <c>main</c>, with one new file <c>watch-fixture/watch-green/fixture.txt</c>,
/// and a pull request from it to <c>main</c>. <c>watch-fixture.yml</c> leaves the check runs <c>build</c>, with the
/// conclusion <c>success</c>, and <c>skipped</c>. Then a review comment by the owner on line 1 of the file, on the head commit.</item>
/// <item>The same for the branch <c>watch-red/fixture</c> and the file <c>watch-fixture/watch-red/fixture.txt</c>.
/// The check run <c>build</c> has the conclusion <c>failure</c>.</item>
/// <item>An issue with a title alone and no label, closed.</item>
/// </list>
/// Each pull request stays open. <see cref="Core.PullRequests.OpenAsync"/> finds an open pull request by its head branch, and
/// <c>PullRequestsIntegrationTests</c> reads the empty fixture through it.
/// </remarks>
public static class ScratchFixtures
{
    public const string EmptyBranch = "fixture/empty";
    public const int EmptyPull = 5804;
    public const int GreenPull = 5805;
    public const int RedPull = 5806;
    public const int TitleAloneIssue = 5807;
}
