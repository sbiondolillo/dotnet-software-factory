using System.Text.Json;
using Xunit;

namespace Specs.Unit;

public sealed class ProgressLinesTests
{
    private const string Run = "run --repo sbiondolillo/factory-scratch --issue 12";
    private const string Feedback = "feedback --repo sbiondolillo/factory-scratch --pr 10";
    private static readonly int[] FourBuilds = [1, 2, 3, 4];

    private static async Task<(SettleHarness H, List<string> Lines)> StartAsync(string arguments, Action<SettleHarness>? setup = null)
    {
        var h = new SettleHarness();
        setup?.Invoke(h);
        await h.RunAsync(arguments.Split(' '), TestContext.Current.CancellationToken);
        return (h, [.. SettleHarness.Lines(h.Out)]);
    }

    private static string Body(SettleHarness h, int number)
    {
        TimedRequest post = Assert.Single(h.Posts($"/issues/{number}/comments"));
        using var json = JsonDocument.Parse(post.Content!);
        return json.RootElement.GetProperty("body").GetString()!;
    }

    [Fact]
    public async Task LayoutFailureWritesTheLineThenTheBodyAndNoSandboxLine()
    {
        (SettleHarness h, List<string> lines) = await StartAsync(Run, x => x.Lacks.Add("Solution.slnx"));
        using (h)
        {
            Assert.DoesNotContain(h.Commands, c => c.Command is "docker");
            Assert.EndsWith(Body(h, 12).TrimEnd('\n'), h.Out.ToString().TrimEnd('\n'), StringComparison.Ordinal);
            Assert.Contains("layout: failed", lines);
            Assert.DoesNotContain(lines, l => l.StartsWith("https://", StringComparison.Ordinal));
            Assert.DoesNotContain("watch: start", lines);
        }
    }

    [Fact]
    public async Task PlanFailureWritesTheLineThenTheBodyAndNoBuildLine()
    {
        (SettleHarness h, List<string> lines) = await StartAsync(Run, x => x.PlanWritesOnlyThePlan());
        using (h)
        {
            Assert.Contains("plan: failed", lines);
            Assert.DoesNotContain("build: start", lines);
            Assert.EndsWith(Body(h, 12).TrimEnd('\n'), h.Out.ToString().TrimEnd('\n'), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RedGateWritesFailedRedoBuildTogether()
    {
        (SettleHarness h, List<string> lines) = await StartAsync(Run, x => x.RedBuilds.Add(1));
        using (h)
        {
            int at = lines.IndexOf("test: failed");
            Assert.Equal(["test: failed", "redo: 1", "build: start"], lines.Skip(at).Take(3));
        }
    }

    [Fact]
    public async Task BlockedReviewWritesBlockedRedoBuildTogether()
    {
        (SettleHarness h, List<string> lines) = await StartAsync(Run, x => x.BlockReview(1));
        using (h)
        {
            int at = lines.IndexOf("review: blocked");
            Assert.Equal(["review: blocked", "redo: 1", "build: start"], lines.Skip(at).Take(3));
        }
    }

    [Fact]
    public async Task SpentBudgetWritesThreeRedosAndEndsWithTheBody()
    {
        (SettleHarness h, List<string> lines) = await StartAsync(Run, x =>
        {
            foreach (int build in FourBuilds)
            {
                x.RedBuilds.Add(build);
            }
        });
        using (h)
        {
            Assert.Equal(["redo: 1", "redo: 2", "redo: 3"], lines.Where(l => l.StartsWith("redo:", StringComparison.Ordinal)));
            Assert.EndsWith(Body(h, 12).TrimEnd('\n'), h.Out.ToString().TrimEnd('\n'), StringComparison.Ordinal);
            Assert.DoesNotContain(lines, l => l.StartsWith("https://", StringComparison.Ordinal));
            Assert.DoesNotContain("watch: start", lines);
        }
    }

    [Fact]
    public async Task SpentBudgetOfFeedbackWritesTheBodyBeforeWatch()
    {
        (SettleHarness h, List<string> lines) = await StartAsync(Feedback, x =>
        {
            x.Threads = "always";
            foreach (int build in FourBuilds)
            {
                x.RedBuilds.Add(build);
            }
        });
        using (h)
        {
            string output = h.Out.ToString();
            string body = Body(h, 10);
            Assert.True(output.IndexOf(body, StringComparison.Ordinal) < output.IndexOf("watch: start", StringComparison.Ordinal));
            Assert.Equal(["redo: 1", "redo: 2", "redo: 3"], lines.Where(l => l.StartsWith("redo:", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task FeedbackWithoutThreadsWritesNoCloneLine()
    {
        (SettleHarness h, List<string> lines) = await StartAsync(Feedback);
        using (h)
        {
            Assert.Equal(["feedback: start", "watch: start", "checks: green, unresolved threads: 0"], lines);
        }
    }

    [Fact]
    public async Task LongEvidenceBodyOnStandardOutputEqualsTheBodyPosted()
    {
        (SettleHarness h, List<string> _) = await StartAsync(Run, x =>
        {
            foreach (int build in FourBuilds)
            {
                x.RedBuilds.Add(build);
            }

            x.FailureText = new string('a', 300000);
        });
        using (h)
        {
            string body = Body(h, 12);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(body) <= 262144);
            Assert.EndsWith(body.TrimEnd('\n'), h.Out.ToString().TrimEnd('\n'), StringComparison.Ordinal);
        }
    }
}
