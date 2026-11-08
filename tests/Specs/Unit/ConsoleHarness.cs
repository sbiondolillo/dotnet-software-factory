using System.Net;
using System.Text;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>
/// The console program over a GitHub that answers each request with <see cref="Status"/>, and a runner that records each command
/// and runs nothing. It fakes no run: a test of the parse, the environment checks or a refused read ends before a run starts, and a
/// backend that the program starts throws.
/// </summary>
public sealed class ConsoleHarness
{
    public ConsoleHarness()
    {
        Environment["FACTORY_GITHUB_TOKEN"] = SettleHarness.Token;
        Environment["ANTHROPIC_API_KEY"] = "secret-key";
    }

    public Dictionary<string, string> Environment { get; } = [];

    /// <summary>The status code of the answer to each request.</summary>
    public int Status { get; set; } = 404;

    /// <summary>The path of each request GitHub got, in order.</summary>
    public List<string> Requests { get; } = [];

    public List<CommandSpec> Commands { get; } = [];

    public StringWriter Out { get; } = new();

    public StringWriter Error { get; } = new();

    public Task<int> RunAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var runner = new Mock<CommandRunner>(MockBehavior.Strict);
        runner.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                Commands.Add(spec);
                return Task.FromResult(new CommandResult());
            });
        var host = new ConsoleHost(
            name => Environment.GetValueOrDefault(name),
            runner.Object,
            new Handler(this),
            Out,
            Error,
            Path.GetTempPath(),
            (_, _) => Task.CompletedTask,
            TimeProvider.System,
            _ => (_, _) => throw new InvalidOperationException("A test of the console starts no run."),
            SettleSettings.Default);
        return ConsoleProgram.RunAsync(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), host, cancellationToken);
    }

    private sealed class Handler(ConsoleHarness harness) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            harness.Requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)harness.Status)
            {
                Content = new StringContent("{\"message\":\"refused\"}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
