using Core;
using Xunit;

namespace Specs.Unit;

public sealed class JqShimTests : IDisposable
{
    private const string Lock = """{"resources":{"./a.md":{"id":"agent_1"},"./b.yaml":{"id":"env_2"}},"flag":false}""";
    private readonly string _root = Directory.CreateTempSubdirectory("jq-shim-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private async Task<CommandResult> JqAsync(string arguments)
    {
        await JqShim.InstallAsync(Path.Combine(_root, "bin"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_root, "in.json"), Lock, TestContext.Current.CancellationToken);
        return await new CommandRunner().RunAsync(
            new CommandSpec
            {
                Command = "bash",
                Arguments = ["-c", "jq " + arguments + " in.json"],
                WorkingDirectory = _root,
                EnvironmentVariables = new Dictionary<string, string> { ["PATH"] = Path.Combine(_root, "bin") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") },
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("-r '.resources[\"./a.md\"].id'", "agent_1\n")]
    [InlineData("'.resources[\"./a.md\"].id'", "\"agent_1\"\n")]
    [InlineData("-r '.resources[\"./b.yaml\"].id, \"x\"'", "env_2\nx\n")]
    [InlineData("-r '\"id=\" + (.resources[\"./a.md\"].id // \"none\")'", "id=agent_1\n")]
    [InlineData("-r '\"id=\" + (.resources[\"./missing\"].id // \"none\")'", "id=none\n")]
    [InlineData("-r '.flag // \"fallback\"'", "fallback\n")]
    [InlineData("-r '.resources[\"./missing\"].id // error(\"no\")' || true", "")]
    public async Task ItPrintsWhatJqPrintsForTheSubsetTheStepUses(string arguments, string expected)
    {
        CommandResult result = await JqAsync(arguments);

        Assert.Equal(expected, result.StandardOutput);
    }

    [Fact]
    public async Task AnErrorExitsWithTheCode5AfterTheOutputsBeforeIt()
    {
        CommandResult result = await JqAsync("-r '\"first\", (.resources[\"./missing\"].id // error(\"claude-lock.json has no id\"))'");

        Assert.Equal(5, result.ExitCode);
        Assert.Equal("first\n", result.StandardOutput);
        Assert.Contains("claude-lock.json has no id", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyntaxOutsideTheSubsetExitsWithTheCode3()
    {
        CommandResult result = await JqAsync("'.resources | keys'");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("unsupported", result.StandardError, StringComparison.Ordinal);
    }
}
