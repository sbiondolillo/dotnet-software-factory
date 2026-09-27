using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FeatureFilesScenarioTitlesSteps(ExceptionState exceptionState)
{
    private string _path = "";
    private IReadOnlyList<FeatureFileScenarios> _result = [];

    [Given("a feature file with the scenarios {string} and {string}")]
    public void GivenAFeatureFileWithTheScenarios(string first, string second)
        => _path = WriteFeatureFile($"Feature: F\n  Scenario: {first}\n    Given a\n  Scenario: {second}\n    Given a\n");

    [Given("a feature file with a Background and the scenario {string}")]
    public void GivenAFeatureFileWithABackgroundAndTheScenario(string scenario)
        => _path = WriteFeatureFile($"Feature: F\n  Background:\n    Given a\n  Scenario: {scenario}\n    Given a\n");

    [Given("a feature file with an outline titled {string} and two example rows")]
    public void GivenAFeatureFileWithAnOutlineTitledAndTwoExampleRows(string title)
        => _path = WriteFeatureFile($"Feature: F\n  Scenario Outline: {title}\n    Given a <who>\n    Examples:\n      | who |\n      | Ada |\n      | Grace |\n");

    [Given("a feature file with the scenario {string} and, under a rule, the scenario {string}")]
    public void GivenAFeatureFileWithTheScenarioAndUnderARuleTheScenario(string first, string second)
        => _path = WriteFeatureFile($"Feature: F\n  Scenario: {first}\n    Given a\n  Rule: R\n    Scenario: {second}\n      Given a\n");

    [Given("an empty feature file")]
    public void GivenAnEmptyFeatureFile() => _path = WriteFeatureFile("");

    [Given("a feature file containing invalid Gherkin text")]
    public void GivenAFeatureFileContainingInvalidGherkinText() => _path = WriteFeatureFile("not valid gherkin at all @#$%");

    [Given("a directory containing the feature file {string} with the scenario {string}")]
    public void GivenADirectoryContainingTheFeatureFileWithTheScenario(string relativePath, string scenario)
    {
        _path = Directory.CreateTempSubdirectory().FullName;
        WriteFileInDirectory(relativePath, FeatureText(scenario));
    }

    [Given("the directory containing the feature file {string} with the scenario {string}")]
    public void GivenTheDirectoryContainingTheFeatureFileWithTheScenario(string relativePath, string scenario)
        => WriteFileInDirectory(relativePath, FeatureText(scenario));

    [Given("the directory containing the file {string}")]
    public void GivenTheDirectoryContainingTheFile(string relativePath)
        => WriteFileInDirectory(relativePath, "not valid gherkin at all @#$%");

    [When("I read the scenario titles of that file with FeatureFiles")]
    public void WhenIReadTheScenarioTitlesOfThatFileWithFeatureFiles() => ReadScenarioTitles();

    [When("I read the scenario titles of that directory with FeatureFiles")]
    public void WhenIReadTheScenarioTitlesOfThatDirectoryWithFeatureFiles() => ReadScenarioTitles();

    [Then("FeatureFiles returns the titles {string} and {string} in that order")]
    public void ThenFeatureFilesReturnsTheTitlesInThatOrder(string first, string second)
        => Assert.Equal([first, second], _result.Single().Titles);

    [Then("FeatureFiles returns the one title {string}")]
    public void ThenFeatureFilesReturnsTheOneTitle(string title)
        => Assert.Equal([title], _result.Single().Titles);

    [Then("FeatureFiles returns an empty list of titles")]
    public void ThenFeatureFilesReturnsAnEmptyListOfTitles() => Assert.Empty(_result.Single().Titles);

    [Then("FeatureFiles returns the titles of {string} and {string}, and reads only those two files")]
    public void ThenFeatureFilesReturnsTheTitlesOfAndReadsOnlyThoseTwoFiles(string first, string second)
    {
        Assert.Equal(2, _result.Count);
        string firstSuffix = first.Replace('/', Path.DirectorySeparatorChar);
        string secondSuffix = second.Replace('/', Path.DirectorySeparatorChar);
        Assert.Contains(_result, entry => entry.Path.EndsWith(firstSuffix, StringComparison.Ordinal));
        Assert.Contains(_result, entry => entry.Path.EndsWith(secondSuffix, StringComparison.Ordinal));
        Assert.Contains(_result, entry => entry.Titles.Contains("The greeter greets by name"));
        Assert.Contains(_result, entry => entry.Titles.Contains("The greeter greets by rule"));
    }

    [Then("FeatureFiles throws a FeatureFileException whose message names the path of the file and holds the parser's message")]
    public void ThenFeatureFilesThrowsAFeatureFileExceptionWhoseMessageNamesThePathOfTheFileAndHoldsTheParserSMessage()
    {
        Assert.IsType<FeatureFileException>(exceptionState.Exception);
        Assert.Contains(_path, exceptionState.Exception!.Message);
        Assert.Contains(ParserMessageFor(_path), exceptionState.Exception.Message);
    }

    private void ReadScenarioTitles()
    {
        try
        {
            _result = FeatureFiles.ReadScenarioTitles(_path);
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    private void WriteFileInDirectory(string relativePath, string content)
    {
        string fullPath = Path.Combine(_path, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    private static string FeatureText(string scenario) => $"Feature: F\n  Scenario: {scenario}\n    Given a\n";

    private static string WriteFeatureFile(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.feature");
        File.WriteAllText(path, text);
        return path;
    }

    private static string ParserMessageFor(string path)
    {
        try
        {
            new Gherkin.Parser().Parse(new StringReader(File.ReadAllText(path)));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        throw new InvalidOperationException("expected the Gherkin parser to refuse this file's text");
    }
}
