using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class AgentOptionSteps(ConsoleProgramSteps console)
{
    private SettleHarness H => console.Harness;

    [Given("the environment holds the variables of the managed agent")]
    public void GivenManaged() => H.AddManagedVariables();

    [Then("docker runs a command")]
    public void ThenDocker() => Assert.Contains(H.Commands, c => c.Command is "docker");

    [Then("the backend of each run gets the settings of the sessions with the agent {string} and the environment {string}")]
    public void ThenSettings(string agent, string environment)
    {
        Assert.NotEmpty(H.BackendOptions);
        Assert.All(H.BackendOptions, options =>
        {
            ManagedAgentsSettings settings = Assert.IsType<ManagedAgentsSettings>(options.Settings);
            Assert.Equal(agent, settings.AgentId);
            Assert.Equal(environment, settings.EnvironmentId);
        });
    }

    [Then("the backend of each run gets no settings of the sessions")]
    public void ThenNoSettings()
    {
        Assert.NotEmpty(H.BackendOptions);
        Assert.All(H.BackendOptions, options => Assert.Null(options.Settings));
    }

    [Then("the backend of each run writes its progress lines to standard output and its log to standard error")]
    public void ThenWriters()
    {
        Assert.NotEmpty(H.BackendOptions);
        Assert.All(H.BackendOptions, options =>
        {
            Assert.Same(H.Out, options.Progress);
            Assert.Same(H.Error, options.Log);
        });
    }
}
