using System.Net;
using System.Text;
using System.Text.Json;

namespace Specs.Steps;

/// <summary>A stand-in for GitHub for the watch: answers the pull request, the check runs, the reviews and the threads.</summary>
public sealed class WatchHandler : HttpMessageHandler
{
    private readonly Dictionary<int, string> _checkRuns = [];
    private readonly Dictionary<int, string> _reviews = [];

    public string Sha { get; set; } = "2878cae3e3bab7f3b1dcdfa14206f2c361021085";

    public int PullStatus { get; set; } = 200;

    public int CheckStatus { get; set; } = 200;

    public int ReviewStatus { get; set; } = 200;

    public int ThreadStatus { get; set; } = 200;

    public string CheckRunsBody { get; set; } = RunsJson([]);

    public string ReviewsBody { get; set; } = ReviewsJson([]);

    public string ThreadsBody { get; set; } = ThreadsJson([]);

    public int PullReads { get; private set; }

    public int CheckRunReads { get; private set; }

    public int ReviewReads { get; private set; }

    public List<RecordedRequest> Requests { get; } = [];

    public static string RunsJson(IEnumerable<(string Name, string Status, string? Conclusion)> runs) =>
        JsonSerializer.Serialize(new { check_runs = runs.Select(r => new { name = r.Name, status = r.Status, conclusion = r.Conclusion }).ToArray() });

    public static string ReviewsJson(IEnumerable<(string? Login, string State, string Commit)> reviews) =>
        JsonSerializer.Serialize(new
        {
            data = new { repository = new { pullRequest = new { reviews = new { nodes = reviews.Select(r => new { state = r.State, author = r.Login is null ? null : new { login = r.Login }, commit = new { oid = r.Commit } }).ToArray() } } } },
        });

    public static string ThreadsJson(IEnumerable<(string Id, bool Resolved, string Path, int? Line, (string Login, string Body)[] Comments)> threads) =>
        JsonSerializer.Serialize(new
        {
            data = new
            {
                repository = new
                {
                    pullRequest = new
                    {
                        reviewThreads = new
                        {
                            nodes = threads.Select(t => new
                            {
                                id = t.Id,
                                isResolved = t.Resolved,
                                path = t.Path,
                                line = t.Line,
                                comments = new { nodes = t.Comments.Select(c => new { author = new { login = c.Login }, body = c.Body }).ToArray() },
                            }).ToArray(),
                        },
                    },
                },
            },
        });

    /// <summary>Sets the answer of the nth read of the check runs, counted from 1.</summary>
    public void CheckRunsAt(int read, string body) => _checkRuns[read] = body;

    /// <summary>Sets the answer of the nth read of the reviews, counted from 1.</summary>
    public void ReviewsAt(int read, string body) => _reviews[read] = body;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, [], content));
        string path = request.RequestUri.AbsolutePath;
        (int status, string body) answer;
        if (path.EndsWith("/check-runs", StringComparison.Ordinal))
        {
            CheckRunReads++;
            answer = (CheckStatus, _checkRuns.GetValueOrDefault(CheckRunReads, CheckRunsBody));
        }
        else if (path.EndsWith("/graphql", StringComparison.Ordinal))
        {
            if (content!.Contains("reviewThreads", StringComparison.Ordinal))
            {
                answer = (ThreadStatus, ThreadsBody);
            }
            else
            {
                ReviewReads++;
                answer = (ReviewStatus, _reviews.GetValueOrDefault(ReviewReads, ReviewsBody));
            }
        }
        else
        {
            PullReads++;
            answer = (PullStatus, JsonSerializer.Serialize(new { head = new { sha = Sha } }));
        }

        return new HttpResponseMessage((HttpStatusCode)answer.status) { Content = new StringContent(answer.body, Encoding.UTF8, "application/json") };
    }
}
