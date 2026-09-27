using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FeatureFilesTestNameSteps
{
    private string _title = "";
    private string _testName = "";

    [Given("the scenario title {string}")]
    public void GivenTheScenarioTitle(string title) => _title = title;

    [When("I compute the test name with FeatureFiles")]
    public void WhenIComputeTheTestNameWithFeatureFiles() => _testName = FeatureFiles.ToTestName(_title);

    [Then("FeatureFiles returns the test name {string}")]
    public void ThenFeatureFilesReturnsTheTestName(string expected) => Assert.Equal(expected, _testName);
}
