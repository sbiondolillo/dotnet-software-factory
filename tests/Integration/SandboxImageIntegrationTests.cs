using System.Text.Json;
using Core;
using Xunit;

namespace Integration;

/// <summary>Proves the docker answers <c>tests/Specs</c> canned still hold against the real <c>docker</c>.</summary>
public sealed class SandboxImageIntegrationTests
{
    private static readonly CommandRunner Runner = new();
    private static readonly string[] Programs = ["node", "claude", "git"];

    private static Task<CommandResult> DockerAsync(params string[] arguments) =>
        Runner.RunAsync(new CommandSpec { Command = "docker", Arguments = arguments }, TestContext.Current.CancellationToken);

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Solution.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution.slnx not found");
    }

    [Fact]
    public async Task InspectOfAMissingImageExitsWithOneAndWritesStandardError()
    {
        CommandResult result = await DockerAsync("image", "inspect", $"factory-missing:{Guid.NewGuid():N}");

        Assert.Equal(1, result.ExitCode);
        Assert.NotEmpty(result.StandardError);
    }

    [Fact]
    public async Task ABuildWithAMissingDockerfileExitsWithOneAndSandboxImageThrowsItsStandardError()
    {
        await Cleanup.RunAsync(async cleanup =>
        {
            DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-sandbox-integration-");
            cleanup.Add($"delete the directory {directory.FullName}", () => directory.Delete(recursive: true));
            string path = Path.Combine(directory.FullName, "sandbox", "Dockerfile");
            CommandResult result = await DockerAsync("build", "--tag", $"factory-nofile:{Guid.NewGuid():N}", "--file", path, directory.FullName);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.StandardOutput);
            Assert.NotEmpty(result.StandardError);

            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""", TestContext.Current.CancellationToken);
            SandboxImageException exception = await Assert.ThrowsAsync<SandboxImageException>(
                () => new SandboxImage(Runner).EnsureAsync($"factory-nofile:{Guid.NewGuid():N}", directory.FullName, TestContext.Current.CancellationToken));
            Assert.NotEmpty(exception.Message.Replace("docker build failed:", "", StringComparison.Ordinal).Trim());
        });
    }

    [Fact]
    public async Task ABuiltImageHoldsTheFiveThings()
    {
        string root = RepoRoot();
        string tag = $"factory-sandbox-test:{Guid.NewGuid():N}";
        await Cleanup.RunAsync(async cleanup =>
        {
            DirectoryInfo copy = Directory.CreateTempSubdirectory("factory-sandbox-copy-");
            cleanup.Add($"delete the directory {copy.FullName}", () => copy.Delete(recursive: true));
            Directory.CreateDirectory(Path.Combine(copy.FullName, "sandbox"));
            File.Copy(Path.Combine(root, "global.json"), Path.Combine(copy.FullName, "global.json"));
            File.Copy(Path.Combine(root, "sandbox", "Dockerfile"), Path.Combine(copy.FullName, "sandbox", "Dockerfile"));
            Directory.CreateDirectory(Path.Combine(copy.FullName, "sandbox", "ApiPrinter"));
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "sandbox", "ApiPrinter")))
            {
                File.Copy(file, Path.Combine(copy.FullName, "sandbox", "ApiPrinter", Path.GetFileName(file)));
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "global.json"), TestContext.Current.CancellationToken));
            string version = document.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;

            var recording = new RecordingRunner();
            var image = new SandboxImage(recording);
            cleanup.Add($"remove the image {tag}", async () => Assert.Equal(0, (await DockerAsync("image", "rm", "--force", tag)).ExitCode));
            await image.EnsureAsync(tag, copy.FullName, TestContext.Current.CancellationToken);
            Assert.Equal(["image", "build"], recording.Runs.Select(run => run.Spec.Arguments[0]));
            CommandResult build = recording.Runs[1].Result;
            Assert.Equal(0, build.ExitCode);
            Assert.Empty(build.StandardOutput);
            CommandResult inspect = await DockerAsync("image", "inspect", tag);
            Assert.Equal(0, inspect.ExitCode);
            recording.Runs.Clear();
            await image.EnsureAsync(tag, copy.FullName, TestContext.Current.CancellationToken);
            Assert.Equal(["image"], recording.Runs.Select(run => run.Spec.Arguments[0]));

            CommandResult dotnet = await DockerAsync("run", "--rm", tag, "dotnet", "--version");
            Assert.Equal(version, dotnet.StandardOutput.Trim());
            foreach (string program in Programs)
            {
                Assert.Equal(0, (await DockerAsync("run", "--rm", tag, program, "--version")).ExitCode);
            }

            CommandResult id = await DockerAsync("run", "--rm", tag, "id", "-u");
            Assert.NotEqual(0, int.Parse(id.StandardOutput.Trim(), System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(0, (await DockerAsync("run", "--rm", tag, "sh", "-c", "test -d \"$HOME/.config\"")).ExitCode);
            CommandResult printer = await DockerAsync("run", "--rm", tag, "dotnet", "/opt/api-printer/ApiPrinter.dll");
            Assert.Equal(2, printer.ExitCode);
            Assert.Equal("usage: ApiPrinter <directory>...", printer.StandardError.Trim());
        });
    }

    private sealed class RecordingRunner : CommandRunner
    {
        public List<(CommandSpec Spec, CommandResult Result)> Runs { get; } = [];

        public override async Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken cancellationToken = default)
        {
            CommandResult result = await base.RunAsync(spec, cancellationToken).ConfigureAwait(false);
            Runs.Add((spec, result));
            return result;
        }
    }
}
