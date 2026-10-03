using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GreetingSteps
{
    private string _name = "";
    private string _greeting = "";

    [Given("the name {string}")]
    public void GivenTheName(string name) => _name = name;

    [When("the greeter greets")]
    public void WhenTheGreeterGreets() => _greeting = Greeter.Greet(_name);

    [Then("the greeting is {string}")]
    public void ThenTheGreetingIs(string expected) => Assert.Equal(expected, _greeting);
}
