using Core;
using Xunit;

namespace Specs.Unit;

/// <summary>
/// A session keeps the model of its first prompt. The builder's session takes the plan prompt first, so the build of a managed
/// factory run takes the plan model, and the program gives every phase one model. A run test on the managed path states that one
/// model in its own setup.
/// </summary>
public sealed class ManagedAgentsSessionModelTests : IDisposable
{
    private readonly ManagedAgentsHarness _harness = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ALaterPromptWithTheModelOfTheFirstGoesToTheSameSession()
    {
        ManagedAgentsAgent agent = _harness.Build(SessionRole.Builder, 1500);
        await agent.RunAsync(new AgentRequest(FakeAgent.Whole("Plan."), "plan-model", 20, Phase.Plan, []), Ct);

        await agent.RunAsync(new AgentRequest(FakeAgent.Whole("Build."), "plan-model", 60, Phase.Build, []), Ct);

        ApiRequest created = Assert.Single(_harness.Posts("/v1/sessions"));
        Assert.Equal("plan-model", created.Json!["agent"]!["model"]!["id"]!.GetValue<string>());
        Assert.Single(_harness.Posts($"/v1/sessions/{_harness.SessionId}/events"));
    }

    [Fact]
    public async Task ALaterPromptWithAnotherModelThrowsNamingBothAndSendsNoRequest()
    {
        ManagedAgentsAgent agent = _harness.Build(SessionRole.Builder, 1500);
        await agent.RunAsync(new AgentRequest(FakeAgent.Whole("Plan."), "plan-model", 20, Phase.Plan, []), Ct);
        int sent = _harness.Requests.Count;

        ClaudeCodeException exception = await Assert.ThrowsAsync<ClaudeCodeException>(
            () => agent.RunAsync(new AgentRequest(FakeAgent.Whole("Build."), "build-model", 60, Phase.Build, []), Ct));

        Assert.Contains("runs the model plan-model, and the build prompt asks for the model build-model", exception.Message, StringComparison.Ordinal);
        Assert.Equal(sent, _harness.Requests.Count);
    }
}
