using System.Text.Json;

namespace Core;

/// <summary>Builds the sandbox image from the repo's Dockerfile when it is missing.</summary>
public sealed class SandboxImage(CommandRunner runner)
{
    /// <summary>Ensures the image <paramref name="tag"/> exists, building it from <c>sandbox/Dockerfile</c> under <paramref name="directory"/> when it does not.</summary>
    public async Task EnsureAsync(string tag, string directory, CancellationToken cancellationToken = default)
    {
        CommandResult inspect = await runner.RunAsync(
            new CommandSpec { Command = "docker", Arguments = ["image", "inspect", tag] },
            cancellationToken).ConfigureAwait(false);
        if (inspect.ExitCode is 0)
        {
            return;
        }

        string version = ReadSdkVersion(directory);
        CommandResult build = await runner.RunAsync(
            new CommandSpec
            {
                Command = "docker",
                Arguments =
                [
                    "build",
                    "--tag", tag,
                    "--file", Path.Combine(directory, "sandbox", "Dockerfile"),
                    "--build-arg", $"DOTNET_SDK_VERSION={version}",
                    directory,
                ],
            },
            cancellationToken).ConfigureAwait(false);
        if (build.ExitCode is not 0)
        {
            throw new SandboxImageException(tag, build.StandardError);
        }
    }

    private static string ReadSdkVersion(string directory)
    {
        string path = Path.Combine(directory, "global.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("sdk", out JsonElement sdk)
            && sdk.ValueKind is JsonValueKind.Object
            && sdk.TryGetProperty("version", out JsonElement version)
            && version.ValueKind is JsonValueKind.String
            && version.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        throw new InvalidOperationException($"{path} has no sdk.version");
    }
}
