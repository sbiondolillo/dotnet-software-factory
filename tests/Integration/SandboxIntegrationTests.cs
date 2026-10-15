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

    private static async Task GitAsync(string directory, params string[] arguments)
    {
        CommandResult result = await Runner.RunAsync(
            new CommandSpec
            {
                Command = "git",
                Arguments = arguments,
                WorkingDirectory = directory,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_NAME"] = "t",
                    ["GIT_AUTHOR_EMAIL"] = "t@example.com",
                    ["GIT_COMMITTER_NAME"] = "t",
                    ["GIT_COMMITTER_EMAIL"] = "t@example.com",
                },
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
    }

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
        await Cleanup.RunAsync(async cleanup =>
        {
            CommandResult ok = await DockerAsync("run", "--detach", "--init", Tag, "sleep", "infinity");
            Assert.Equal(0, ok.ExitCode);
            string id = ok.StandardOutput.Trim();
            cleanup.Add($"remove the container {id}", async () => Assert.Equal(0, (await DockerAsync("rm", "--force", id)).ExitCode));
            Assert.Matches("^[0-9a-f]{64}\n$", ok.StandardOutput);
            Assert.Empty(ok.StandardError);
        });

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
    public async Task TheGitDirectoryOfACloneIsReadOnlyInTheContainerExceptItsWorktrees()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string origin = Path.Combine(_tree.FullName, "origin");
        string clone = Path.Combine(_tree.FullName, "clone");
        Directory.CreateDirectory(origin);
        await GitAsync(origin, "init", "--quiet");
        await File.WriteAllTextAsync(Path.Combine(origin, "tracked.txt"), "head", token);
        await GitAsync(origin, "add", "-A");
        await GitAsync(origin, "commit", "--quiet", "-m", "head");
        await GitAsync(_tree.FullName, "clone", "--quiet", origin, clone);
        string config = await File.ReadAllTextAsync(Path.Combine(clone, ".git", "config"), token);

        await using Sandbox sandbox = await Sandbox.StartAsync(Runner, Tag, RepoRoot(), clone, token);

        CommandResult hook = await ExecAsync(sandbox, "sh", ["-c", "printf 'exit 0' > .git/hooks/pre-push"]);
        Assert.NotEqual(0, hook.ExitCode);
        Assert.Contains("Read-only file system", hook.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(clone, ".git", "hooks", "pre-push")));

        CommandResult fsmonitor = await ExecAsync(sandbox, "git", ["config", "core.fsmonitor", "true"]);
        Assert.NotEqual(0, fsmonitor.ExitCode);
        Assert.Equal(config, await File.ReadAllTextAsync(Path.Combine(clone, ".git", "config"), token));

        CommandResult tree = await ExecAsync(sandbox, "sh", ["-c", "printf made > /work/written"]);
        Assert.Equal(0, tree.ExitCode);

        string copy = $"/tmp/check-{Guid.NewGuid():N}";
        CommandResult add = await ExecAsync(sandbox, "git", ["worktree", "add", "--detach", copy, "HEAD"]);
        Assert.Equal(0, add.ExitCode);
        Assert.Equal("head", (await ExecAsync(sandbox, "cat", [$"{copy}/tracked.txt"])).StandardOutput);
        Assert.Equal(0, (await ExecAsync(sandbox, "git", ["worktree", "remove", "--force", copy])).ExitCode);
    }

    [Fact]
    public async Task RemoveForceEndsARunningCommandAndTheContainerIsGone()
    {
        await EnsureImageAsync();
        await Cleanup.RunAsync(async cleanup =>
        {
            CommandResult run = await DockerAsync("run", "--detach", "--init", Tag, "sleep", "infinity");
            Assert.Equal(0, run.ExitCode);
            string id = run.StandardOutput.Trim();
            cleanup.Add($"remove the container {id} when docker holds it", async () =>
            {
                if ((await DockerAsync("container", "inspect", id)).ExitCode is 0)
                {
                    Assert.Equal(0, (await DockerAsync("rm", "--force", id)).ExitCode);
                }
            });

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
        });
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
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
