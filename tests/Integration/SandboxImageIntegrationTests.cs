using System.Globalization;
using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the docker answers <c>tests/Specs</c> canned still hold against a real docker.</summary>
public sealed class SandboxImageIntegrationTests
{
    private static readonly CommandRunner Runner = new();

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "global.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("global.json not found");
    }

    private static Task<CommandResult> DockerAsync(params string[] arguments)
        => Runner.RunAsync(new CommandSpec { Command = "docker", Arguments = arguments }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task BuildsTheRealImageAndItHoldsTheToolsAndANonRootUser()
    {
        string tag = $"factory-sandbox-it-{Guid.NewGuid():N}";
        string root = RepoRoot();
        try
        {
            CommandResult missing = await DockerAsync("image", "inspect", tag);
            Assert.Equal(1, missing.ExitCode);
            Assert.NotEmpty(missing.StandardError);

            CommandResult noFile = await DockerAsync("build", "--file", Path.Combine(root, "no-such-dir", "Dockerfile"), root);
            Assert.Equal(1, noFile.ExitCode);
            Assert.Empty(noFile.StandardOutput);
            Assert.NotEmpty(noFile.StandardError);

            await new SandboxImage(Runner).EnsureAsync(tag, root, TestContext.Current.CancellationToken);
            Assert.Equal(0, (await DockerAsync("image", "inspect", tag)).ExitCode);

            using JsonDocument global = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "global.json"), TestContext.Current.CancellationToken));
            string version = global.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
            CommandResult dotnet = await DockerAsync("run", "--rm", tag, "dotnet", "--version");
            Assert.Equal(version, dotnet.StandardOutput.Trim());

            Assert.Equal(0, (await DockerAsync("run", "--rm", tag, "node", "--version")).ExitCode);
            Assert.Equal(0, (await DockerAsync("run", "--rm", tag, "claude", "--version")).ExitCode);
            Assert.Equal(0, (await DockerAsync("run", "--rm", tag, "git", "--version")).ExitCode);

            CommandResult id = await DockerAsync("run", "--rm", tag, "id", "-u");
            Assert.NotEqual(0, int.Parse(id.StandardOutput.Trim(), CultureInfo.InvariantCulture));

            CommandResult config = await DockerAsync("run", "--rm", tag, "sh", "-c", "test -d \"$HOME/.config\"");
            Assert.Equal(0, config.ExitCode);
        }
        finally
        {
            await DockerAsync("rmi", "--force", tag);
        }
    }
}
