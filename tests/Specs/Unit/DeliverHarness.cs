using System.Net;
using System.Text;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>A Deliver over a mock CommandRunner and a stub GitHub, recording what each receives.</summary>
public sealed class DeliverHarness : IDisposable
{
    private readonly StubHandler _handler;

    public DeliverHarness(string? failingSubcommand = null, int postStatus = 201, int commentStatus = 201)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(m => m.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                Commands.Add(spec);
                return Task.FromResult(new CommandResult { ExitCode = spec.Arguments[0] == failingSubcommand ? 1 : 0, StandardError = "boom" });
            });
        _handler = new StubHandler(postStatus, commentStatus);
        var http = new HttpClient(_handler);
        var client = new GitHubClient(http, "token", TimeSpan.Zero);
        Deliver = new Deliver(new Git(mock.Object), new PullRequests(client), client);
    }

    public Deliver Deliver { get; }

    public List<CommandSpec> Commands { get; } = [];

    public List<(string Method, string Path, string? Content)> Requests => _handler.Requests;

    public Task<PullRequest> RunAsync() =>
        Deliver.DeliverAsync("/work", "a1b2c3d4", "o", "r", "main", 12, "Title", "tok");

    public void Dispose() => _handler.Dispose();

    private sealed class StubHandler(int postStatus, int commentStatus) : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Content)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method.Method, path, content));
            (int status, string body) = request.Method == HttpMethod.Get
                ? (200, "[]")
                : path.EndsWith("/comments", StringComparison.Ordinal)
                    ? (commentStatus, "{}")
                    : (postStatus, "{\"number\":10,\"html_url\":\"https://github.com/o/r/pull/10\"}");
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
