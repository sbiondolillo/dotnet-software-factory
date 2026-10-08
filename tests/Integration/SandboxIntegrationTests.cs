using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the docker answers <c>tests/Specs</c> canned for <see cref="Sandbox"/> still hold against the real <c>docker</c>.</summary>
public sealed class SandboxIntegrationTests : IDisposable
{
    private const string Tag = "factory-sandbox:integration";
    private static readonly CommandRunner Runner = new();
    private readonly DirectoryInfo _tree = Directory.CreateTempSubdirectory("factory-sandbox-tree-");

    public void Dispose() => _tree.Delete(recursive: true);

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution.slnx not found");
    }

    private static Task<CommandResult> DockerAsync(params string[] arguments) =>
        Runner.RunAsync(new CommandSpec { Command = "docker", Arguments = arguments }, TestContext.Current.CancellationToken);

    private static Task EnsureImageAsync() =>
        new SandboxImage(Runner).EnsureAsync(Tag, RepoRoot(), TestContext.Current.CancellationToken);

    private Task<Sandbox> StartAsync() =>
        Sandbox.StartAsync(Runner, Tag, RepoRoot(), _tree.FullName, TestContext.Current.CancellationToken);

    private static Task<CommandResult> ExecAsync(Sandbox sandbox, string program, string[] arguments, string[]? names = null) =>
        sandbox.RunAsync(program, arguments, names ?? [], TestContext.Current.CancellationToken);

    private static string Id(string uidOrGid)
    {
        return File.ReadLines("/proc/self/status")
            .First(line => line.StartsWith(uidOrGid + ":", StringComparison.Ordinal))
            .Split('\t', StringSplitOptions.RemoveEmptyEntries)[1];
    }

    [Fact]
    public async Task RunPrintsA64DigitIdAndAMissingTagExitsWith125()
    {
        await EnsureImageAsync();
        CommandResult ok = await DockerAsync("run", "--detach", "--init", Tag, "sleep", "infinity");
        try
        {
            Assert.Equal(0, ok.ExitCode);
            Assert.Matches("^[0-9a-f]{64}\n$", ok.StandardOutput);
            Assert.Empty(ok.StandardError);
        }
        finally
        {
            await DockerAsync("rm", "--force", ok.StandardOutput.Trim());
        }

        CommandResult missing = await DockerAsync("run", "--detach", $"factory-missing:{Guid.NewGuid():N}", "true");
        Assert.Equal(125, missing.ExitCode);
        Assert.NotEmpty(missing.StandardError);
        Assert.Empty(missing.StandardOutput);
    }

    [Fact]
    public async Task ExecAnswersHoldInTheContainer()
    {
        await using Sandbox sandbox = await StartAsync();

        Assert.Equal("/work\n", (await ExecAsync(sandbox, "pwd", [])).StandardOutput);
        Assert.Equal(Id("Uid") + "\n", (await ExecAsync(sandbox, "id", ["-u"])).StandardOutput);

        CommandResult home = await ExecAsync(sandbox, "sh", ["-c", "test -d \"$HOME/.config\" && touch \"$HOME/probe\" && echo ok"]);
        Assert.Equal("ok\n", home.StandardOutput);

        await ExecAsync(sandbox, "sh", ["-c", "printf made > /work/written"]);
        string path = Path.Combine(_tree.FullName, "written");
        Assert.Equal("made", File.ReadAllText(path));
        Assert.Equal(Id("Uid"), (await ExecAsync(sandbox, "stat", ["-c", "%u", "/work/written"])).StandardOutput.Trim());

        CommandResult key = await ExecAsync(sandbox, "sh", ["-c", "test -n \"${ANTHROPIC_API_KEY+set}\""]);
        Assert.Equal(0, key.ExitCode);

        Environment.SetEnvironmentVariable("FACTORY_SANDBOX_PROBE", "v");
        try
        {
            CommandResult held = await ExecAsync(sandbox, "printenv", ["FACTORY_SANDBOX_PROBE"], ["FACTORY_SANDBOX_PROBE"]);
            Assert.Equal("v\n", held.StandardOutput);
            Assert.Equal(1, (await ExecAsync(sandbox, "printenv", ["FACTORY_SANDBOX_PROBE"])).ExitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTORY_SANDBOX_PROBE", null);
        }

        CommandResult seven = await ExecAsync(sandbox, "sh", ["-c", "echo out; echo err 1>&2; exit 7"]);
        Assert.Equal(7, seven.ExitCode);
        Assert.Equal("out\n", seven.StandardOutput);
        Assert.Equal("err\n", seven.StandardError);

        CommandResult missing = await ExecAsync(sandbox, "factory-no-such-program", []);
        Assert.Equal(127, missing.ExitCode);
        Assert.NotEmpty(missing.StandardOutput);
        Assert.Empty(missing.StandardError);
    }

    [Fact]
    public async Task RemoveForceEndsARunningCommandAndTheContainerIsGone()
    {
        await EnsureImageAsync();
        CommandResult run = await DockerAsync("run", "--detach", "--init", Tag, "sleep", "infinity");
        string id = run.StandardOutput.Trim();

        CommandResult exec = await DockerAsync("exec", "--detach", id, "sleep", "600");
        Assert.Equal(0, exec.ExitCode);
        Assert.Empty(exec.StandardOutput);

        await WaitForAsync(() => HostHasProcess("sleep", "600"), 5_000);
        Assert.True(HostHasProcess("sleep", "600"));

        CommandResult removed = await DockerAsync("rm", "--force", id);

        await WaitForAsync(() => !HostHasProcess("sleep", "600"), 5_000);
        Assert.False(HostHasProcess("sleep", "600"));

        Assert.Equal(id + "\n", removed.StandardOutput);
        Assert.Equal(1, (await DockerAsync("container", "inspect", id)).ExitCode);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static bool HostHasProcess(params string[] arguments)
    {
        foreach (string directory in Directory.EnumerateDirectories("/proc"))
        {
            string? name = Path.GetFileName(directory);
            if (name is null || name.Length is 0 || name.Any(character => !char.IsDigit(character)))
            {
                continue;
            }

            if (ReadArguments(Path.Combine(directory, "cmdline")).SequenceEqual(arguments))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ReadArguments(string path)
    {
        try
        {
            return File.ReadAllText(path).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (SystemException)
        {
            return [];
        }
    }
}
