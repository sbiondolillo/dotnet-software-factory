using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Core;

/// <summary>What a <see cref="FeedbackRun"/> needs: the pull request, the clone, the models and the author.</summary>
public sealed record FeedbackRunRequest(
    string Owner,
    string Repo,
    int PullNumber,
    string Token,
    string Directory,
    string ImageTag,
    string RootDirectory,
    string BuildModel,
    int BuildTurns,
    string ReviewModel,
    int ReviewTurns,
    string AuthorName,
    string AuthorEmail,
    IReadOnlyList<string> SecretNames,
    bool SkipIntegration = false);

/// <summary>The outcome of a <see cref="FeedbackRun"/>: the threads resolved, the threads left unresolved and the Redos spent.</summary>
public sealed record FeedbackRunResult(int Resolved, int Unresolved, int Redos);

/// <summary>Answers every unresolved review thread of a pull request within three Redos.</summary>
public sealed class FeedbackRun(CommandRunner runner, GitHubClient client, TextWriter? progress = null)
{
    private const int RedoBudget = 3;
    private const string ReportPath = ".factory/threads.json";

    /// <summary>Builds an answer to each unresolved thread, gates and reviews it, pushes it, and replies to each thread.</summary>
    public async Task<FeedbackRunResult> RunAsync(FeedbackRunRequest request, CancellationToken cancellationToken = default)
    {
        var reviewThreads = new ReviewThreads(client);
        IReadOnlyList<ReviewThread> all = await reviewThreads.ReadThreadsAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false);
        List<ReviewThread> threads = [.. all.Where(thread => !thread.IsResolved)];
        if (threads.Count is 0)
        {
            return new FeedbackRunResult(0, 0, 0);
        }

        string branch = await new PullRequests(client).ReadHeadBranchAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false);
        var pullRequests = new PullRequests(client);
        IReadOnlyList<string> scope = [];
        string? check = null;
        bool cleanup = false;
        bool refactor = false;
        if (PullRequests.ParseClosingIssue(await pullRequests.ReadBodyAsync(request.Owner, request.Repo, request.PullNumber, cancellationToken).ConfigureAwait(false)) is int issueNumber)
        {
            GitHubIssue issue = await client.ReadIssueAsync(request.Owner, request.Repo, issueNumber, cancellationToken).ConfigureAwait(false);
            if (issue.Labels.Contains("cleanup"))
            {
                cleanup = true;
                scope = CleanupBody.ReadScope(issue.Body);
                check = CleanupBody.ReadCheck(issue.Body);
            }
            else if (issue.Labels.Contains("refactor"))
            {
                refactor = true;
                check = CleanupBody.ReadCheck(issue.Body);
            }
        }

        var git = new Git(runner);
        await Say("clone: start").ConfigureAwait(false);
        await git.CloneAsync($"https://github.com/{request.Owner}/{request.Repo}.git", request.Token, request.Directory, cancellationToken).ConfigureAwait(false);
        await git.CheckoutExistingBranchAsync(branch, request.Directory, cancellationToken).ConfigureAwait(false);
        string baseCommit = await git.ReadHeadShaAsync(request.Directory, cancellationToken).ConfigureAwait(false);
        Pin pin = refactor ? Pin.TakeRefactor(request.Directory) : Pin.Take(request.Directory);

        string title = $"Answer the review threads of pull request #{request.PullNumber.ToString(CultureInfo.InvariantCulture)}";
        string body = Describe(threads);
        string reportFile = Path.Combine(request.Directory, ".factory", "threads.json");

        await Say("sandbox: start").ConfigureAwait(false);
        Sandbox sandbox = await Sandbox.StartAsync(runner, request.ImageTag, request.RootDirectory, request.Directory, cancellationToken).ConfigureAwait(false);
        await using (sandbox.ConfigureAwait(false))
        {
            var agent = new ClaudeCodeAgent(sandbox);
            var buildPhase = new BuildPhase(agent, cleanup, refactor, readsPlan: false);
            var lintGate = new LintGate(sandbox, git);
            var testGate = new TestGate(sandbox, skipIntegration: request.SkipIntegration);
            var reviewPhase = new ReviewPhase(agent, git, cleanup, refactor, readsPlan: false);

            IReadOnlySet<string> basePassed = cleanup
                ? await testGate.ReadPassedTestsAsync(request.Directory, cancellationToken).ConfigureAwait(false)
                : new HashSet<string>();
            int redos = 0;
            IReadOnlyList<string> redoTexts = [];
            IReadOnlyList<ReportItem> items = [];
            while (true)
            {
                if (File.Exists(reportFile))
                {
                    File.Delete(reportFile);
                }

                await Say("build: start").ConfigureAwait(false);
                await buildPhase.RunAsync(title, body, request.BuildModel, request.BuildTurns, redoTexts, cancellationToken).ConfigureAwait(false);
                (items, IReadOnlyList<string> faults) = ReadReport(reportFile, threads);
                if (faults.Count > 0)
                {
                    redoTexts = faults;
                }
                else
                {
                    await Say("lint: start").ConfigureAwait(false);
                    await lintGate.RunAsync(request.Directory, title, request.AuthorName, request.AuthorEmail, cancellationToken).ConfigureAwait(false);
                    IReadOnlyList<string> scopeFaults = cleanup
                        ? await CleanupGate.ScopeFaultsAsync(git, baseCommit, request.Directory, scope, cancellationToken).ConfigureAwait(false)
                        : [];
                    if (scopeFaults.Count > 0)
                    {
                        redoTexts = scopeFaults;
                    }
                    else
                    {
                        await Say("test: start").ConfigureAwait(false);
                        TestGateResult gate = cleanup
                            ? await testGate.RunAsync(request.Directory, pin, request.SecretNames, path => CleanupGate.PinExempt(scope, path), cancellationToken).ConfigureAwait(false)
                            : await testGate.RunAsync(request.Directory, pin, request.SecretNames, cancellationToken).ConfigureAwait(false);
                        await Say(gate.Passed ? "test: passed" : "test: failed").ConfigureAwait(false);
                        IReadOnlyList<string> gateFaults = gate.Passed ? [] : gate.Evidence;
                        if (gate.Passed && (cleanup || refactor))
                        {
                            gateFaults = cleanup ? CleanupGate.LostTests(basePassed, gate) : [];
                            if (gateFaults.Count is 0 && check is not null)
                            {
                                CommandResult after = await CleanupGate.RunCheckAsync(sandbox, check, progress, cancellationToken).ConfigureAwait(false);
                                await Say(after.ExitCode is 0 ? "check: green" : "check: red").ConfigureAwait(false);
                                gateFaults = after.ExitCode is 0 ? [] : [CleanupGate.CheckRedo(after)];
                            }
                        }

                        if (gateFaults.Count > 0)
                        {
                            redoTexts = gateFaults;
                        }
                        else
                        {
                            await Say("review: start").ConfigureAwait(false);
                            ReviewPhaseResult review = await reviewPhase
                                .RunAsync(title, body, request.ReviewModel, request.ReviewTurns, request.Directory, baseCommit, cancellationToken).ConfigureAwait(false);
                            await Say(review.Approved ? "review: approved" : "review: blocked").ConfigureAwait(false);
                            if (review.Approved)
                            {
                                break;
                            }

                            redoTexts = review.Blocking;
                        }
                    }
                }

                if (redos >= RedoBudget)
                {
                    string comment = CommentText.Fit($"The budget of {RedoBudget} Redos is spent. The last build still fails:", redoTexts);
                    await Say(comment).ConfigureAwait(false);
                    await client.PostCommentAsync(request.Owner, request.Repo, request.PullNumber, comment, cancellationToken).ConfigureAwait(false);
                    return new FeedbackRunResult(0, threads.Count, redos);
                }

                redos++;
                await Say($"redo: {redos}").ConfigureAwait(false);
            }

            await git.PushAsync(branch, request.Directory, request.Token, cancellationToken).ConfigureAwait(false);
            int resolved = 0;
            foreach (ReviewThread thread in threads)
            {
                ReportItem item = items.First(i => i.Id == thread.Id);
                await reviewThreads.ReplyAsync(thread.Id, item.Reply, cancellationToken).ConfigureAwait(false);
                if (item.Fixed)
                {
                    await reviewThreads.ResolveAsync(thread.Id, cancellationToken).ConfigureAwait(false);
                    resolved++;
                }
            }

            return new FeedbackRunResult(resolved, threads.Count - resolved, redos);
        }
    }

    private Task Say(string line) => ProgressLine.WriteAsync(progress, line);

    private static string Describe(IReadOnlyList<ReviewThread> threads)
    {
        var text = new StringBuilder("Answer each review thread below.\n");
        foreach (ReviewThread thread in threads)
        {
            text.Append("\nThread ").Append(thread.Id).Append('\n').Append("Path: ").Append(thread.Path).Append('\n');
            if (thread.Line is int line)
            {
                text.Append("Line: ").Append(line.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            foreach (ReviewThreadComment comment in thread.Comments)
            {
                text.Append("Comment by ").Append(comment.Author).Append(":\n").Append(comment.Body).Append('\n');
            }
        }

        text.Append("\nWhen the work is done, write the report ").Append(ReportPath)
            .Append(", one JSON object with the key threads: a list with one item for each thread above. ")
            .Append("Each item has an id (the thread id), a status and a reply. ")
            .Append("The status fixed means that you changed the work tree to answer the thread. ")
            .Append("The status disputed means that you leave the work tree as it is on that point. ")
            .Append("The reply is the text for the reviewer.");
        return text.ToString();
    }

    private static (IReadOnlyList<ReportItem> Items, IReadOnlyList<string> Faults) ReadReport(string file, IReadOnlyList<ReviewThread> threads)
    {
        if (!File.Exists(file))
        {
            return ([], [$"The report {ReportPath} is absent. Write it."]);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ([], [$"The report {ReportPath} is not JSON. Write one JSON object with a threads list."]);
        }

        if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("threads", out JsonElement list) || list.ValueKind is not JsonValueKind.Array)
        {
            return ([], [$"The report {ReportPath} is not one JSON object with a threads list."]);
        }

        var objects = list.EnumerateArray().Where(item => item.ValueKind is JsonValueKind.Object).ToList();
        var items = new List<ReportItem>();
        var faults = new List<string>();
        foreach (ReviewThread thread in threads)
        {
            JsonElement? found = objects.Cast<JsonElement?>().FirstOrDefault(item => item!.Value.TryGetProperty("id", out JsonElement id) && id.ValueKind is JsonValueKind.String && id.GetString() == thread.Id);
            if (found is not JsonElement element)
            {
                faults.Add($"The report {ReportPath} has no item for the thread {thread.Id}.");
                continue;
            }

            string? status = element.TryGetProperty("status", out JsonElement s) && s.ValueKind is JsonValueKind.String ? s.GetString() : null;
            bool statusOk = status is "fixed" or "disputed";
            if (!statusOk)
            {
                string shown = element.TryGetProperty("status", out JsonElement raw) ? raw.ToString() : "missing";
                faults.Add($"The item of the thread {thread.Id} has the status {shown}. The status is fixed or disputed.");
            }

            string? reply = element.TryGetProperty("reply", out JsonElement r) && r.ValueKind is JsonValueKind.String ? r.GetString() : null;
            bool replyOk = !string.IsNullOrWhiteSpace(reply);
            if (!replyOk)
            {
                faults.Add($"The item of the thread {thread.Id} has no reply. The reply holds a character other than whitespace.");
            }

            if (statusOk && replyOk)
            {
                items.Add(new ReportItem(thread.Id, status is "fixed", reply!));
            }
        }

        return (items, faults);
    }

    private sealed record ReportItem(string Id, bool Fixed, string Reply);
}
