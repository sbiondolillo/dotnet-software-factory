using System.Net;
using System.Text;
using Core;
using Moq;

namespace Specs.Unit;

/// <summary>
/// A <see cref="ManagedBackend"/> alone, as a run drives it through the seam: its start, the prompts of its builder and its reviewer,
/// its collect step and its end. It starts through <see cref="AgentBackend.Pick"/>, as the program starts it. Below it, a mock <see cref="CommandRunner"/> answers git and docker, and <see cref="ManagedRunFake"/>
/// answers the Managed Agents API and the token routes of the GitHub App. The two <see cref="FakeAgent"/>s play the agents of the
/// sessions: the request that a test sends is pending on its fake while the adapter carries it to the fake API.
/// </summary>
public sealed class ManagedBackendHarness : IDisposable
{
    public const string SessionBranch = "factory/a1b2c3d4-session";

    private readonly DirectoryInfo _clone = Directory.CreateTempSubdirectory("managed-backend-clone-");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("managed-backend-root-");
    private AgentBackend? _backend;

    public ManagedBackendHarness()
    {
        Api = new ManagedRunFake { OnMessage = (_, _) => Play() };
        Builder = new FakeAgent(_clone.FullName);
        Reviewer = new FakeAgent(_clone.FullName);
    }

    /// <summary>The fake of the Managed Agents API and of the token routes.</summary>
    public ManagedRunFake Api { get; }

    /// <summary>The agent of the builder's session.</summary>
    public FakeAgent Builder { get; }

    /// <summary>The agent of the reviewer's session.</summary>
    public FakeAgent Reviewer { get; }

    private const string BaseCommit = "4f2a9c1e8b7d6053a1f2e3d4c5b6a7980f1e2d3c";

    private const string Title = "Add a checkout total";

    /// <summary>True when git reports a change in the work tree.</summary>
    public bool WorkTreeChanged { get; set; }

    public string CloneDirectory => _clone.FullName;

    public List<CommandSpec> Commands { get; } = [];

    /// <summary>Each git command, as its words, <c>docker run</c>, each command in the container, as <c>exec</c> and its words,
    /// and each request to the fake API, as its method and path, in order.</summary>
    public List<string> Timeline { get; } = [];

    /// <summary>What the backend wrote to its progress writer.</summary>
    public StringWriter Progress { get; } = new();

    /// <summary>What the backend wrote to its log.</summary>
    public StringWriter Log { get; } = new();

    /// <summary>The clock of the backend. It moves by <see cref="AnswerSeconds"/> at each answer of a session.</summary>
    public StepClock Clock { get; } = new();

    /// <summary>The seconds each answer of a session takes on <see cref="Clock"/>.</summary>
    public double AnswerSeconds { get; set; }

    /// <summary>What the progress writer held when the <c>docker run</c> started, or null before it.</summary>
    public string? ProgressAtDockerRun { get; private set; }

    /// <summary>Each answer that the backend reported to the run, with the time it took.</summary>
    public List<(TimeSpan Elapsed, AgentAnswer Answer)> Reported { get; } = [];

    /// <summary>The answer of each prompt, in order.</summary>
    public List<AgentAnswer> Answers { get; } = [];

    /// <summary>The backend, after <see cref="StartAsync"/>.</summary>
    public AgentBackend Backend => _backend ?? throw new InvalidOperationException("The backend has not started.");

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        StartBackend start = AgentBackend.Pick(new BackendOptions(
            mock.Object, "factory-sandbox:test", _root.FullName, ManagedRunFake.Settings(), "sbiondolillo", "factory-scratch", "token",
            "Factory", "factory@example.com", new Handler(this), (_, _) => Task.CompletedTask, Log, Clock, Progress));
        var git = new Git(mock.Object);
        _backend = await start(
            new BackendStart(git, _clone.FullName, BaseCommit, SessionBranch, Title, (elapsed, answer) => Reported.Add((elapsed, answer))),
            cancellationToken);
    }

    /// <summary>Sends one prompt of <paramref name="phase"/> to the builder, or to the reviewer for a review, as the run does.</summary>
    public async Task<AgentAnswer> PromptAsync(Phase phase, string model = "claude-sonnet-5-5", CancellationToken cancellationToken = default)
    {
        (FakeAgent fake, Agent agent) = phase is Phase.Review ? (Reviewer, Backend.Reviewer) : (Builder, Backend.Builder);
        var request = new AgentRequest(FakeAgent.Whole($"The {phase.Text()} prompt."), model, 10, phase, []);
        fake.Pending = request;
        try
        {
            AgentAnswer answer = await agent.RunAsync(request, cancellationToken);
            Answers.Add(answer);
            return answer;
        }
        finally
        {
            fake.Pending = null;
        }
    }

    public void Dispose()
    {
        _backend?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Progress.Dispose();
        Log.Dispose();
        _clone.Delete(recursive: true);
        _root.Delete(recursive: true);
    }

    /// <summary>The turn of the session for the request that is pending: the answer of its fake, each output of the answer in the
    /// files of the session, and a push of a commit for a build unless the build called <see cref="ManagedRunFake.HoldHead"/>.</summary>
    private ManagedTurn Play()
    {
        Clock.Advance(AnswerSeconds);
        FakeAgent agent = Builder.Pending is not null ? Builder : Reviewer;
        Phase phase = agent.Pending!.Phase;
        AgentAnswer answer = agent.Play();
        foreach ((string name, string text) in answer.Outputs)
        {
            Api.AddOutput(name, text);
        }

        int cents = (int)decimal.Round(answer.CostUsd * 100m, MidpointRounding.AwayFromZero);
        return new ManagedTurn(answer.Result, answer.IsError, cents, Pushes: phase is Phase.Build && !Api.TakeHold());
    }

    private CommandResult Answer(CommandSpec spec)
    {
        Commands.Add(spec);
        if (spec.Command is "git")
        {
            Timeline.Add(string.Join(' ', ["git", .. spec.Arguments]));
            return spec.Arguments[0] switch
            {
                "status" => new CommandResult { StandardOutput = WorkTreeChanged ? "?? specs/features/\n" : "" },
                "pull" => Pulled(),
                "ls-remote" => new CommandResult { StandardOutput = Api.LsRemote(spec.Arguments) },
                "ls-files" => new CommandResult { StandardOutput = string.Concat(Api.TrackedFactoryFiles.Select(file => file + "\n")) },
                _ => new CommandResult(),
            };
        }

        switch (spec.Arguments[0])
        {
            case "run":
                Timeline.Add("docker run");
                ProgressAtDockerRun = Progress.ToString();
                return new CommandResult { StandardOutput = GateSandbox.ContainerId + "\n" };
            case "exec":
                List<string> arguments = [.. spec.Arguments];
                Timeline.Add(string.Join(' ', ["exec", .. arguments[(arguments.IndexOf(GateSandbox.ContainerId) + 1)..]]));
                return new CommandResult();
            case "rm":
                Timeline.Add("docker rm");
                return new CommandResult();
            default:
                return new CommandResult();
        }
    }

    private CommandResult Pulled()
    {
        Api.Pull(_clone.FullName);
        return new CommandResult();
    }

    private sealed class Handler(ManagedBackendHarness harness) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? content = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Uri uri = request.RequestUri!;
            if (!ManagedRunFake.Handles(uri))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }

            harness.Timeline.Add($"{request.Method.Method} {uri.AbsolutePath}");
            (int code, string answer) = harness.Api.Route(request.Method.Method, uri, content);
            return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }
}
