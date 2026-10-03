using Core;
using Specs.Steps;
using Xunit;

namespace Specs.Unit;

/// <summary>Builds a copy of the starter in a temporary directory and runs its program, so the starter's command tree is proved as the user gets it.</summary>
public sealed class StarterCommandTreeTests
{
    [Fact]
    public async Task TheStarterProgramGreetsAndRefusesAnExtraArgument()
    {
        string root = StarterChecks.RepoRoot();
        DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-starter-tree-");
        try
        {
            CopyTree(Path.Combine(root, "templates/starter"), directory.FullName);
            string project = Path.Combine(directory.FullName, "src/App/App.csproj");
            CommandResult build = await DotnetAsync(directory.FullName, "build", "--no-restore", project);
            if (build.ExitCode is not 0)
            {
                build = await DotnetAsync(directory.FullName, "build", project);
            }

            Assert.True(build.ExitCode is 0, build.StandardOutput);
            string dll = Directory.GetFiles(Path.Combine(directory.FullName, "src/App/bin"), "dotnet-software-factory.dll", SearchOption.AllDirectories).Single();

            CommandResult ada = await DotnetAsync(directory.FullName, dll, "Ada");
            CommandResult none = await DotnetAsync(directory.FullName, dll);
            CommandResult extra = await DotnetAsync(directory.FullName, dll, "Ada", "Lovelace");
            CommandResult help = await DotnetAsync(directory.FullName, dll, "--help");

            Assert.Equal((0, "Hello, Ada!"), (ada.ExitCode, ada.StandardOutput.Trim()));
            Assert.Equal((0, "Hello, world!"), (none.ExitCode, none.StandardOutput.Trim()));
            Assert.Equal(1, extra.ExitCode);
            Assert.Contains("Unrecognized command or argument 'Lovelace'.", extra.StandardError, StringComparison.Ordinal);
            Assert.Contains("<name>", help.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("[default: world]", help.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static Task<CommandResult> DotnetAsync(string directory, params string[] arguments) =>
        new CommandRunner().RunAsync(
            new CommandSpec { Command = "dotnet", Arguments = arguments, WorkingDirectory = directory },
            TestContext.Current.CancellationToken);

    private static void CopyTree(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
