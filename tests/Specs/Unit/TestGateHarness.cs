using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

/// <summary>One command the mock saw: the dotnet subcommand, its arguments and the names given by <c>--env</c>.</summary>
internal sealed record SeenCommand(string Subcommand, IReadOnlyList<string> Arguments, IReadOnlyList<string> EnvNames);

/// <summary>A real temp work tree with <c>specs/features/checkout.feature</c>, a <see cref="Sandbox"/> over a mock
/// <see cref="CommandRunner"/> and the canned answer of each dotnet command.</summary>
internal sealed class TestGateHarness : IDisposable
{
    public const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string Feature = PlanPhaseHarness.SoundFeature;

    public static readonly (string Class, string Method, string Outcome, string? Message) PassingFeatureTest =
        ("Specs.Features.CheckoutFeature", "TheTotalIsTheSumOfThePrices", "Passed", null);

    public static readonly (string Class, string Method, string Outcome, string? Message) PassingUnitTest =
        ("Specs.Unit.CheckoutTests", "AddsTwoPrices", "Passed", null);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factory-testgate-root-");
    private Sandbox? _sandbox;

    public Sandbox? Sandbox => _sandbox;

    public TestGateHarness(string feature = Feature, string featurePath = "specs/features/checkout.feature")
    {
        WorkTree = Directory.CreateTempSubdirectory("factory-testgate-tree-");
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(WorkTree.FullName, featurePath))!);
        File.WriteAllText(Path.Combine(WorkTree.FullName, featurePath), feature);
        File.WriteAllText(Path.Combine(WorkTree.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        Pin = Pin.Take(WorkTree.FullName);
        foreach (string subcommand in new[] { "restore", "build", "format", "test", "run" })
        {
            Answers[subcommand] = new CommandResult();
        }
    }

    public DirectoryInfo WorkTree { get; }

    public Pin Pin { get; }

    public Dictionary<string, CommandResult> Answers { get; } = [];

    /// <summary>The tests the test command writes to a TRX file, or null when it writes none.</summary>
    public List<(string Class, string Method, string Outcome, string? Message)>? Trx { get; set; } = [PassingFeatureTest, PassingUnitTest];

    public List<SeenCommand> Seen { get; } = [];

    public int DockerCommands { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Answer(spec)));
        _sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _root.FullName, WorkTree.FullName, cancellationToken);
        Seen.Clear();
        DockerCommands = 0;
    }

    public Task<TestGateResult> RunAsync(IReadOnlyList<string> names, CancellationToken cancellationToken) =>
        RunAsync(names, false, cancellationToken);

    public Task<TestGateResult> RunAsync(IReadOnlyList<string> names, bool skipIntegration, CancellationToken cancellationToken) =>
        new TestGate(_sandbox!, skipIntegration: skipIntegration).RunAsync(WorkTree.FullName, Pin, names, cancellationToken);

    public void WriteEarlierTrx(string outcome)
    {
        string directory = Path.Combine(WorkTree.FullName, "tests", "Specs", "TestResults");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "earlier.trx"),
            Steps.TrxBuilder.Build([("Specs.Unit.OldTests", "Old", outcome, outcome == "Passed" ? null : "old failure")]));
        Directory.CreateDirectory(Path.Combine(WorkTree.FullName, "TestResults"));
        File.Copy(Path.Combine(directory, "earlier.trx"), Path.Combine(WorkTree.FullName, "TestResults", "earlier.trx"));
    }

    public void Dispose()
    {
        _sandbox?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _root.Delete(recursive: true);
        WorkTree.Delete(recursive: true);
    }

    private CommandResult Answer(CommandSpec spec)
    {
        if (spec.Command is not "docker")
        {
            return new CommandResult();
        }

        DockerCommands++;
        if (spec.Arguments[0] is "run")
        {
            return new CommandResult { StandardOutput = ContainerId + "\n" };
        }

        if (spec.Arguments[0] is not "exec")
        {
            return new CommandResult();
        }

        List<string> arguments = [.. spec.Arguments];
        int idIndex = arguments.IndexOf(ContainerId);
        List<string> names = [];
        for (int i = 1; i < idIndex; i += 2)
        {
            names.Add(arguments[i + 1]);
        }

        string subcommand = arguments[idIndex + 2];
        Seen.Add(new SeenCommand(subcommand, arguments[(idIndex + 3)..], names));
        if (subcommand is "test" && Trx is not null)
        {
            int at = arguments.IndexOf("--results-directory");
            string directory = Path.Combine(WorkTree.FullName, arguments[at + 1]["/work/".Length..]);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "this-run.trx"), Steps.TrxBuilder.Build(Trx));
        }

        return Answers[subcommand];
    }
}
