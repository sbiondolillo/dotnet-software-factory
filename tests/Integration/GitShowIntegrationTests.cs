using Core;
using Xunit;

namespace Integration;

/// <summary>Proves that <c>git show &lt;base&gt;:&lt;path&gt;</c>, which the build suite answers from a canned result, behaves as <see cref="Git.ReadFileAtAsync"/> assumes.</summary>
public sealed class GitShowIntegrationTests
{
    [Fact]
    public async Task ShowGivesTheFileAtTheBaseAndNullForAFileTheBaseLacks()
    {
        var runner = new CommandRunner();
        var git = new Git(runner);
        await Cleanup.RunAsync(async cleanup =>
        {
            DirectoryInfo directory = Directory.CreateTempSubdirectory("factory-git-show-");
            cleanup.Add($"delete the directory {directory.FullName}", () => directory.Delete(recursive: true));
            string dir = directory.FullName;
            await Run(runner, dir, "init", "-q");
            Directory.CreateDirectory(Path.Combine(dir, "specs/features"));
            await File.WriteAllTextAsync(Path.Combine(dir, "specs/features/a.feature"), "Feature: A\n", TestContext.Current.CancellationToken);
            await git.CommitAllAsync(dir, "base", "Factory", "factory@example.com", TestContext.Current.CancellationToken);
            string baseCommit = await git.ReadHeadShaAsync(dir, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(dir, "specs/features/a.feature"), "Feature: B\n", TestContext.Current.CancellationToken);

            Assert.Equal("Feature: A\n", await git.ReadFileAtAsync(baseCommit, "specs/features/a.feature", dir, TestContext.Current.CancellationToken));
            Assert.Null(await git.ReadFileAtAsync(baseCommit, "specs/features/none.feature", dir, TestContext.Current.CancellationToken));
        });
    }

    private static async Task Run(CommandRunner runner, string directory, params string[] arguments)
    {
        CommandResult result = await runner.RunAsync(new CommandSpec { Command = "git", Arguments = arguments, WorkingDirectory = directory }, TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
    }
}
