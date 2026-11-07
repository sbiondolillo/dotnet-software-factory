using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

/// <summary>
/// A <see cref="BuildLoop"/> over a fake build, fake gates and a fake review, with the lint over a <see cref="Sandbox"/> of a
/// mock <see cref="CommandRunner"/>, and no <see cref="AgentBackend"/>. Each fake takes the next answer of its script, and the last answer repeats. Every call
/// goes to <see cref="Calls"/>: <c>build</c>, <c>lint</c>, the text of a gate, or <c>review</c>. A build moves the clock 5
/// seconds, the lint 1 second, a gate 2 seconds, and the review notes one prompt of 7 seconds, 3 turns and $0.5.
/// </summary>
public sealed class BuildLoopHarness : IAsyncDisposable
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("build-loop-root-");
    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("build-loop-work-");
    private readonly Dictionary<GateName, int> _gateCalls = [];
    private Sandbox? _sandbox;
    private int _builds;
    private int _reviews;
    private int _lints;

    public BuildLoopHarness()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Progress = new CancellingWriter(Source);
        Summary = new JobSummary(Progress);
    }

    /// <summary>The gates of the loop, in order.</summary>
    public List<GateName> Gates { get; } = [];

    /// <summary>The answers of the build, one for each call.</summary>
    public List<Func<BuildOutcome>> Builds { get; } = [Pass];

    /// <summary>The answers of each gate, one for each call. A gate with no script passes with the result <c>passed</c>.</summary>
    public Dictionary<GateName, List<Func<GateOutcome>>> GateAnswers { get; } = [];

    /// <summary>The answers of the review, one for each call.</summary>
    public List<Func<ReviewPhaseResult>> Reviews { get; } = [Approve];

    /// <summary>The answers of <c>dotnet format</c> in the lint, one for each call.</summary>
    public List<Func<CommandResult>> Formats { get; } = [() => new CommandResult()];

    /// <summary>The answer of <c>git status --porcelain</c> after the format: a line here makes the lint commit.</summary>
    public string Status { get; set; } = "";

    /// <summary>Every call of the loop, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The Redo texts that each build got, in order.</summary>
    public List<IReadOnlyList<string>> BuildTexts { get; } = [];

    /// <summary>A progress line whose write cancels <see cref="Source"/>, or null for none.</summary>
    public string? CancelOnLine
    {
        get => Progress.CancelOn;
        set => Progress.CancelOn = value;
    }

    public CancellingWriter Progress { get; }

    public JobSummary Summary { get; }

    public StepClock Clock { get; } = new();

    public CancellationTokenSource Source { get; } = new();

    public BuildLoopResult? Result { get; private set; }

    public Exception? Thrown { get; private set; }

    /// <summary>The progress lines, in order.</summary>
    public IReadOnlyList<string> Lines => [.. Progress.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];

    /// <summary>The rows of the job summary, after its header.</summary>
    public IReadOnlyList<string> Rows => [.. Summary.Render(FactoryRun.SummaryHeading).Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Skip(1)];

    /// <summary>The Redo items of the job summary.</summary>
    public IReadOnlyList<string> RedoItems => [.. Summary.Render(FactoryRun.SummaryHeading).Split('\n').Where(line => line.StartsWith("- Redo ", StringComparison.Ordinal))];

    public static BuildOutcome Pass() => new(null, [], []);

    public static ReviewPhaseResult Approve() => new(true, []);

    public static Func<BuildOutcome> BuildStop(string text) => () => new BuildOutcome(text, [], []);

    public static Func<BuildOutcome> BuildRedo(params string[] texts) => () => new BuildOutcome(null, texts, []);

    /// <summary>A build that passes with <paramref name="faults"/>, the faults that the agent knows about it.</summary>
    public static Func<BuildOutcome> BuildFaults(params string[] faults) => () => new BuildOutcome(null, [], faults);

    public static Func<GateOutcome> GateFails(params string[] texts) => () => new GateOutcome("failed", texts);

    public static Func<ReviewPhaseResult> ReviewBlocks(params string[] texts) => () => new ReviewPhaseResult(false, texts);

    public static Func<ReviewPhaseResult> ReviewError(string text) => () => new ReviewPhaseResult(false, [text], IsError: true);

    /// <summary>An answer that cancels <see cref="Source"/> and then gives <paramref name="answer"/>.</summary>
    public Func<T> Cancels<T>(Func<T> answer) => () =>
    {
        Source.Cancel();
        return answer();
    };

    /// <summary>Starts the sandbox and runs the loop once, keeping its result or the exception it threw.</summary>
    public async Task RunAsync()
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Reply(spec)));
        _sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _root.FullName, _work.FullName, TestContext.Current.CancellationToken);
        var lint = new LintGate(_sandbox, new Git(mock.Object));
        var loop = new BuildLoop(Summary, Clock, Progress, lint, _work.FullName, "Add a total", "Ada", "ada@example.com");
        try
        {
            Result = await loop.RunAsync(new FakeBuild(this), [.. Gates.Select(name => new FakeGate(this, name))], ReviewAsync, Source.Token);
        }
        catch (Exception exception)
        {
            Thrown = exception;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_sandbox is not null)
        {
            await _sandbox.DisposeAsync();
        }

        Source.Dispose();
        Progress.Dispose();
        _root.Delete(recursive: true);
        _work.Delete(recursive: true);
    }

    private static T Next<T>(List<Func<T>> script, int call) => script[Math.Min(call, script.Count - 1)]();

    private Task<ReviewPhaseResult> ReviewAsync(CancellationToken cancellationToken)
    {
        Calls.Add("review");
        Summary.Prompt(TimeSpan.FromSeconds(7), 3, 0.5m);
        return Task.FromResult(Next(Reviews, _reviews++));
    }

    private CommandResult Reply(CommandSpec spec)
    {
        if (spec.Command is "git")
        {
            switch (spec.Arguments[0])
            {
                case "status":
                    return new CommandResult { StandardOutput = Status };
                default:
                    return new CommandResult();
            }
        }

        if (spec.Command is not "docker")
        {
            return new CommandResult();
        }

        if (spec.Arguments[0] is "run")
        {
            return new CommandResult { StandardOutput = ContainerId + "\n" };
        }

        if (spec.Arguments[0] is "exec" && spec.Arguments.Contains("format"))
        {
            Calls.Add("lint");
            Clock.Advance(1);
            return Next(Formats, _lints++);
        }

        return new CommandResult();
    }

    private sealed class FakeBuild(BuildLoopHarness harness) : IBuild
    {
        public Task<BuildOutcome> RunAsync(IReadOnlyList<string> redoTexts, CancellationToken cancellationToken = default)
        {
            harness.Calls.Add("build");
            harness.BuildTexts.Add(redoTexts);
            harness.Clock.Advance(5);
            return Task.FromResult(Next(harness.Builds, harness._builds++));
        }
    }

    private sealed class FakeGate(BuildLoopHarness harness, GateName name) : IGate
    {
        public GateName Name => name;

        public Task<GateOutcome> RunAsync(CancellationToken cancellationToken = default)
        {
            harness.Calls.Add(name.Text());
            harness.Clock.Advance(2);
            int call = harness._gateCalls.GetValueOrDefault(name);
            harness._gateCalls[name] = call + 1;
            return Task.FromResult(harness.GateAnswers.TryGetValue(name, out List<Func<GateOutcome>>? script)
                ? Next(script, call)
                : new GateOutcome("passed", []));
        }
    }
}

/// <summary>A writer of progress lines that cancels a token source when it writes the line <see cref="CancelOn"/>.</summary>
public sealed class CancellingWriter(CancellationTokenSource source) : StringWriter(System.Globalization.CultureInfo.InvariantCulture)
{
    public string? CancelOn { get; set; }

    public override Task WriteLineAsync(string? value)
    {
        Task written = base.WriteLineAsync(value);
        if (value is not null && value == CancelOn)
        {
            source.Cancel();
        }

        return written;
    }
}
