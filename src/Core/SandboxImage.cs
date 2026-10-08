using System.Text.Json;

namespace Core;

/// <summary>Makes sure the sandbox image exists, building it from <c>sandbox/Dockerfile</c> when it is missing.</summary>
public sealed class SandboxImage(CommandRunner runner)
{
    /// <summary>Runs nothing further when the image <paramref name="tag"/> exists; otherwise builds it from the
    /// Dockerfile under <paramref name="rootDirectory"/>, with the SDK version of its <c>global.json</c>.</summary>
    public async Task EnsureAsync(string tag, string rootDirectory, CancellationToken cancellationToken = default)
    {
        CommandResult inspect = await runner.RunAsync(
            new CommandSpec { Command = "docker", Arguments = ["image", "inspect", tag] },
            cancellationToken).ConfigureAwait(false);
        if (inspect.ExitCode is 0)
        {
            return;
        }

        string globalJson = await File.ReadAllTextAsync(
            Path.Combine(rootDirectory, "global.json"), cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(globalJson);
        string version = document.RootElement.GetProperty("sdk").GetProperty("version").GetString()
            ?? throw new InvalidOperationException("global.json sdk.version is not a string");

        CommandResult build = await runner.RunAsync(
            new CommandSpec
            {
                Command = "docker",
                Arguments =
                [
                    "build",
                    "--tag", tag,
                    "--file", Path.Combine(rootDirectory, "sandbox", "Dockerfile"),
                    "--build-arg", $"DOTNET_SDK_VERSION={version}",
                    rootDirectory,
                ],
            },
            cancellationToken).ConfigureAwait(false);
        if (build.ExitCode is not 0)
        {
            throw new SandboxImageException(build.StandardError);
        }
    }
}
