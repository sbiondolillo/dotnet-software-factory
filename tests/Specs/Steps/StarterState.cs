using Core;

namespace Specs.Steps;

/// <summary>The template store and the copy of one starter scenario. It removes both when the scenario ends.</summary>
public sealed class StarterState : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("starter-home-").FullName;
    private readonly string _copy = Path.Combine(Directory.CreateTempSubdirectory("starter-copy-").FullName, "copy");

    public int? InstallExit { get; set; }

    public int? NewExit { get; set; }

    public string Copy => _copy;

    public static string Starter => Path.Combine(StarterChecks.RepoRoot(), "templates", "starter");

    public async Task<CommandResult> DotnetAsync(params string[] arguments)
        => await new CommandRunner().RunAsync(new CommandSpec
        {
            Command = "dotnet",
            Arguments = arguments,
            EnvironmentVariables = new() { ["DOTNET_CLI_HOME"] = _home, ["DOTNET_NOLOGO"] = "1" },
            Timeout = TimeSpan.FromMinutes(3),
        }).ConfigureAwait(false);

    public void Dispose()
    {
        Directory.Delete(_home, true);
        Directory.Delete(Path.GetDirectoryName(_copy)!, true);
    }
}
