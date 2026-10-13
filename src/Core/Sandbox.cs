using System.Globalization;
using System.Runtime.InteropServices;

namespace Core;

/// <summary>A running container that holds a work tree at <c>/work</c>, runs commands in it and removes it on disposal.</summary>
public sealed class Sandbox : IAsyncDisposable
{
    private const string Home = "/tmp/sandbox-home";
    private readonly CommandRunner _runner;
    private readonly string _id;

    private Sandbox(CommandRunner runner, string id)
    {
        _runner = runner;
        _id = id;
    }

    /// <summary>Makes sure the image <paramref name="tag"/> exists, then starts a container over <paramref name="workTree"/>.</summary>
    public static async Task<Sandbox> StartAsync(
        CommandRunner runner,
        string tag,
        string rootDirectory,
        string workTree,
        CancellationToken cancellationToken = default)
    {
        await new SandboxImage(runner).EnsureAsync(tag, rootDirectory, cancellationToken).ConfigureAwait(false);

        string user = string.Create(CultureInfo.InvariantCulture, $"{GetUid()}:{GetGid()}");

        // The home is a directory on the host, so the transcript that Claude Code writes under it outlives the container.
        // The process that creates it owns it, and that is the user the container runs as.
        string home = Path.Combine(rootDirectory, ".factory", "sandbox-home", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        List<string> volumes = ["--volume", $"{workTree}:/work", "--volume", $"{home}:{Home}"];
        string gitDirectory = Path.Combine(workTree, ".git");
        if (Directory.Exists(gitDirectory))
        {
            // The agent cannot write a hook or a config line that the git commands of the factory read on the runner.
            // `git worktree add` in the container still writes .git/worktrees, which docker would create as root.
            string worktrees = Path.Combine(gitDirectory, "worktrees");
            Directory.CreateDirectory(worktrees);
            volumes.AddRange(["--volume", $"{gitDirectory}:/work/.git:ro", "--volume", $"{worktrees}:/work/.git/worktrees"]);
        }

        CommandResult run = await runner.RunAsync(
            new CommandSpec
            {
                Command = "docker",
                Arguments =
                [
                    "run",
                    "--detach",
                    "--init",
                    "--user", user,
                    .. volumes,
                    "--workdir", "/work",
                    "--env", $"HOME={Home}",
                    "--env", "ANTHROPIC_API_KEY",
                    tag,
                    "sh", "-c", $"mkdir -p {Home}/.config && exec sleep infinity",
                ],
            },
            cancellationToken).ConfigureAwait(false);
        if (run.ExitCode is not 0)
        {
            throw new SandboxException(run.StandardError);
        }

        return new Sandbox(runner, run.StandardOutput.Trim());
    }

    /// <summary>Runs <paramref name="program"/> in the container, holding each variable of <paramref name="names"/> by name.</summary>
    public Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default) =>
        RunAsync(program, arguments, names, null, cancellationToken);

    /// <summary>Runs <paramref name="program"/> in the container, in <paramref name="workingDirectory"/> when it is not null.</summary>
    public Task<CommandResult> RunAsync(
        string program,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> names,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        List<string> dockerArguments = ["exec"];
        if (workingDirectory is not null)
        {
            dockerArguments.Add("--workdir");
            dockerArguments.Add(workingDirectory);
        }

        foreach (string name in names)
        {
            dockerArguments.Add("--env");
            dockerArguments.Add(name);
        }

        dockerArguments.Add(_id);
        dockerArguments.Add(program);
        dockerArguments.AddRange(arguments);
        return _runner.RunAsync(
            new CommandSpec { Command = "docker", Arguments = dockerArguments },
            cancellationToken);
    }

    /// <summary>Removes the container.</summary>
    public async ValueTask DisposeAsync() =>
        await _runner.RunAsync(
            new CommandSpec { Command = "docker", Arguments = ["rm", "--force", _id] }).ConfigureAwait(false);

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    [DllImport("libc", EntryPoint = "getgid")]
    private static extern uint GetGid();
}
