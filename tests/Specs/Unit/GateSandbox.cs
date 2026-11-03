using Core;
using Moq;

namespace Specs.Unit;

/// <summary>A <see cref="Sandbox"/> over a mock <see cref="CommandRunner"/> for a test of a gate. Each <c>docker exec</c>
/// gets the answer of <see cref="Answer"/> for the words after the container id, and every other command the code 0.</summary>
internal sealed class GateSandbox : IAsyncDisposable
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factory-gate-root-");
    private Sandbox? _sandbox;

    public GateSandbox()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");
        WorkTree = Directory.CreateTempSubdirectory("factory-gate-tree-");
    }

    public DirectoryInfo WorkTree { get; }

    /// <summary>The answer to the words of one command in the container: the program and its arguments.</summary>
    public Func<IReadOnlyList<string>, CommandResult> Answer { get; set; } = _ => new CommandResult();

    /// <summary>The words of each command that ran in the container, in order.</summary>
    public List<IReadOnlyList<string>> Seen { get; } = [];

    /// <summary>Runs <paramref name="gate"/> through <see cref="IGate"/>, as a run does.</summary>
    public static Task<GateOutcome> RunAsync(IGate gate, CancellationToken cancellationToken) => gate.RunAsync(cancellationToken);

    /// <summary>The name of <paramref name="gate"/>, read through <see cref="IGate"/>.</summary>
    public static GateName NameOf(IGate gate) => gate.Name;

    public Sandbox Sandbox => _sandbox ?? throw new InvalidOperationException("The sandbox has not started.");

    public async Task<Sandbox> StartAsync(CancellationToken cancellationToken)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) => Task.FromResult(Reply(spec)));
        _sandbox = await Sandbox.StartAsync(mock.Object, "factory-sandbox:test", _root.FullName, WorkTree.FullName, cancellationToken);
        Seen.Clear();
        return _sandbox;
    }

    public async ValueTask DisposeAsync()
    {
        if (_sandbox is not null)
        {
            await _sandbox.DisposeAsync();
        }

        _root.Delete(recursive: true);
        WorkTree.Delete(recursive: true);
    }

    private CommandResult Reply(CommandSpec spec)
    {
        if (spec.Command is not "docker")
        {
            return new CommandResult();
        }

        if (spec.Arguments[0] is "run")
        {
            return new CommandResult { StandardOutput = ContainerId + "\n" };
        }

        if (spec.Arguments[0] is not "exec")
        {
            return new CommandResult();
        }

        List<string> arguments = [.. spec.Arguments];
        IReadOnlyList<string> words = arguments[(arguments.IndexOf(ContainerId) + 1)..];
        Seen.Add(words);
        return Answer(words);
    }
}
