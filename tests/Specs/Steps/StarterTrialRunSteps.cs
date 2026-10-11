using Reqnroll;

namespace Specs.Steps;

[Binding]
public sealed class StarterTrialRunSteps(StarterState state)
{
    private string _file = "";
    private string _event = "";

    private string Workflow(string name) => Path.Combine(state.Copy, ".github", "workflows", name);

    [Then("the workflow {string} has the trigger, the event {string} with the type {string}")]
    public void ThenTriggerWithType(string file, string eventName, string type) => WorkflowChecks.Trigger(Workflow(file), eventName, type);

    [Then("the workflow {string} has the trigger, the event {string}")]
    public void ThenTrigger(string file, string eventName) => WorkflowChecks.Trigger(Workflow(file), eventName);

    [Then("the trigger of {string} {string} has the required input {string} with the type {string}")]
    public void ThenRequiredInput(string file, string eventName, string input, string type)
    {
        _file = file;
        _event = eventName;
        WorkflowChecks.RequiredInput(Workflow(file), eventName, input, type);
    }

    [Then("it has the input {string} with the default {string}")]
    public void ThenInputDefault(string input, string value) => WorkflowChecks.InputDefault(Workflow(_file), _event, input, value);
}
