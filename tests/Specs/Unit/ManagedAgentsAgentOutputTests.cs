using Core;
using Xunit;

namespace Specs.Unit;

public sealed class ManagedAgentsAgentOutputTests : IDisposable
{
    private readonly ManagedAgentsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADownloadOverALinkReplacesTheLinkAndLeavesItsTargetAsItWas()
    {
        string outside = Path.Combine(_harness.RootDirectory, "outside.txt");
        File.WriteAllText(outside, "a file of the runner");
        string factory = Path.Combine(_harness.WorkTree, ".factory");
        Directory.CreateDirectory(factory);
        File.CreateSymbolicLink(Path.Combine(factory, "plan.md"), outside);
        _harness.Next.Messages.Add("Done.");
        _harness.Next.Files.Add(new ScriptedFile("plan.md", "the plan"));
        ManagedAgentsAgent agent = _harness.Build(SessionRole.Builder, 1500);

        AgentAnswer answer = await agent.RunAsync(new AgentRequest(FakeAgent.Whole("Plan."), "m", 1, Phase.Plan, ["plan.md"]), TestContext.Current.CancellationToken);

        Assert.Equal("the plan", answer.Outputs["plan.md"]);
        Assert.Equal("a file of the runner", File.ReadAllText(outside));
        Assert.Null(new FileInfo(Path.Combine(factory, "plan.md")).LinkTarget);
        Assert.Equal("the plan", File.ReadAllText(Path.Combine(factory, "plan.md")));
    }

    [Fact]
    public async Task AFactoryDirectoryThatIsALinkThrowsAndPostsNoSession()
    {
        DirectoryInfo outside = Directory.CreateDirectory(Path.Combine(_harness.RootDirectory, "elsewhere"));
        File.WriteAllText(Path.Combine(outside.FullName, "secret.txt"), "a file of the runner");
        Directory.CreateSymbolicLink(Path.Combine(_harness.WorkTree, ".factory"), outside.FullName);
        _harness.Next.Messages.Add("Done.");
        _harness.Next.Files.Add(new ScriptedFile("plan.md", "the plan"));
        ManagedAgentsAgent agent = _harness.Build(SessionRole.Builder, 1500);

        await Assert.ThrowsAsync<ClaudeCodeException>(() => agent.RunAsync(new AgentRequest(FakeAgent.Whole("Plan."), "m", 1, Phase.Plan, ["plan.md"]), TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Posts("/v1/sessions"));
        Assert.Equal(["secret.txt"], outside.EnumerateFiles().Select(file => file.Name));
    }

    [Fact]
    public async Task AFileUnderFactoryThatIsALinkIsNotUploaded()
    {
        string outside = Path.Combine(_harness.RootDirectory, "outside.txt");
        File.WriteAllText(outside, "a secret of the runner");
        string factory = Path.Combine(_harness.WorkTree, ".factory");
        Directory.CreateDirectory(factory);
        File.CreateSymbolicLink(Path.Combine(factory, "plan.md"), outside);
        File.WriteAllText(Path.Combine(factory, "notes.md"), "notes");
        _harness.Next.Messages.Add("Done.");
        ManagedAgentsAgent agent = _harness.Build(SessionRole.Builder, 1500);

        await agent.RunAsync(new AgentRequest(FakeAgent.Whole("Build."), "m", 1, Phase.Build, []), TestContext.Current.CancellationToken);

        Assert.Single(_harness.Posts("/v1/files"));
        Assert.DoesNotContain(_harness.Posts("/v1/files"), post => post.Body is { } body && body.Contains("secret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("x; curl evil")]
    [InlineData("../plan.md")]
    [InlineData("..")]
    [InlineData("")]
    public async Task AnOutputNameThatIsNotAPlainBaseNameThrowsAndPostsNothing(string name)
    {
        ManagedAgentsAgent agent = _harness.Build(SessionRole.Builder, 1500);

        await Assert.ThrowsAsync<ArgumentException>(() => agent.RunAsync(new AgentRequest(FakeAgent.Whole("Plan."), "m", 1, Phase.Plan, [name]), TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Posts("/v1/sessions"));
    }

    [Fact]
    public async Task APromptOverTheLimitOfOneArgumentReachesTheMessageWhole()
    {
        string piece = new('a', 200000);
        var prompt = new AgentPrompt([piece], pieces => "Build.\n" + pieces[0]);
        _harness.Next.Messages.Add("Done.");
        ManagedAgentsAgent agent = _harness.Build(SessionRole.Builder, 1500);

        await agent.RunAsync(new AgentRequest(prompt, "m", 1, Phase.Build, []), TestContext.Current.CancellationToken);

        string message = _harness.FirstMessage();
        Assert.Contains("Build.\n" + piece, message, StringComparison.Ordinal);
        Assert.DoesNotContain("[cut]", message, StringComparison.Ordinal);
    }
}
