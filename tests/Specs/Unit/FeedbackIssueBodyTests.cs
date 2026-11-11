using Core;
using Xunit;

namespace Specs.Unit;

/// <summary>The body that a <see cref="Core.FeedbackRun"/> gives the builder and the reviewer, read from each prompt.</summary>
public sealed class FeedbackIssueBodyTests
{
    private const string IssueBody = "## Scope\nREADME.md\n\n## Check\n\n```sh\ngrep -q Feedback README.md\n```\n";

    private const string Threads =
        "\nThread " + RunHarness.ThreadA + "\nPath: src/Core/Checkout.cs\nLine: 14\n"
        + "Comment by sbiondolillo:\nThe total ignores the second item of the cart.\n"
        + "\nThread " + RunHarness.ThreadB + "\nPath: tests/Specs/Unit/CheckoutTests.cs\nLine: 9\n"
        + "Comment by ada:\nName the unit of the total.\n";

    private const string Report =
        "\nWhen the work is done, write the report .factory/threads.json, one JSON object with the key threads: "
        + "a list with one item for each thread above. Each item has an id (the thread id), a status and a reply. "
        + "The status fixed means that you changed the work tree to answer the thread. "
        + "The status disputed means that you leave the work tree as it is on that point. "
        + "The reply is the text for the reviewer.";

    private const string DisputeRule =
        " A thread that asks for a change to a path outside the `## Scope` takes the status `disputed`, and its reply names the scope.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Runs one approved round and gives the build prompt and the review prompt.</summary>
    private static async Task<IReadOnlyList<AgentRequest>> PromptsAsync(int? closingIssue, string? label)
    {
        using var h = new RunHarness { ClosingIssue = closingIssue, Body = IssueBody };
        if (label is not null)
        {
            h.Labels.Add(label);
        }

        await h.RunFeedbackAsync(Ct);
        Assert.Equal(["build", "review"], h.AgentRequests.Select(r => r.Phase.Text()));
        return h.AgentRequests;
    }

    /// <summary>The prompt holds the body whole: it follows the label of the body and ends where the prompt goes on.</summary>
    private static void AssertBody(string expected, IReadOnlyList<AgentRequest> prompts)
    {
        foreach (AgentRequest prompt in prompts)
        {
            Assert.Contains("Issue body:\n" + expected + "\n\n", prompt.Prompt.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ACleanupIssueGivesTheIssueBodyThenTheThreadsThenTheReport()
    {
        IReadOnlyList<AgentRequest> prompts = await PromptsAsync(11, "cleanup");
        AssertBody(
            IssueBody
            + "\n## Review threads\n\n"
            + "The work tree holds the build of the issue above. The threads below are the work. "
            + "Change the work tree only where a thread asks for it.\n"
            + Threads
            + Report
            + DisputeRule,
            prompts);
    }

    [Theory]
    [InlineData("cleanup", true)]
    [InlineData("refactor", false)]
    [InlineData("bug", false)]
    public async Task OnlyACleanupIssueDisputesAThreadOutsideTheScope(string label, bool disputes)
    {
        IReadOnlyList<AgentRequest> prompts = await PromptsAsync(11, label);
        foreach (AgentRequest prompt in prompts)
        {
            Assert.Equal(disputes, prompt.Prompt.Text.Contains(DisputeRule.Trim(), StringComparison.Ordinal));
            Assert.Contains(Report + (disputes ? DisputeRule : "") + "\n\n", prompt.Prompt.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task APullRequestWithNoClosingIssueKeepsTheThreadsAlone()
    {
        IReadOnlyList<AgentRequest> prompts = await PromptsAsync(null, "cleanup");
        AssertBody("Answer each review thread below.\n" + Threads + Report, prompts);
    }
}
