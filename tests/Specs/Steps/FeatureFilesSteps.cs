using Core;
using Gherkin;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FeatureFilesSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("feature-files-").FullName;

    private string? _lastContent;
    private string _scenarioTitle = "";
    private string _testName = "";
    private IReadOnlyList<string> _titles = [];
    private IReadOnlyList<(string RelativePath, IReadOnlyList<string> Titles)> _directoryResults = [];

    [Given("a feature file {string} with the lines:")]
    public void GivenAFeatureFileWithTheLines(string fileName, Table table)
        => WriteFile(fileName, string.Join('\n', table.Rows.Select(row => row["line"])));

    [Given("a feature file {string} with no content")]
    public void GivenAFeatureFileWithNoContent(string fileName) => WriteFile(fileName, "");

    [Given("a feature file {string} with the content:")]
    public void GivenAFeatureFileWithTheContent(string fileName, string content) => WriteFile(fileName, content);

    [Given("a file {string} with the content:")]
    public void GivenAFileWithTheContent(string fileName, string content) => WriteFile(fileName, content);

    [Given("the scenario title {string}")]
    public void GivenTheScenarioTitle(string title) => _scenarioTitle = title;

    [When("FeatureFiles reads the scenario titles of {string}")]
    public void WhenFeatureFilesReadsTheScenarioTitlesOf(string relativePath)
    {
        try
        {
            _titles = FeatureFiles.ReadScenarioTitles(Path.Combine(_root, relativePath));
            exceptionState.Exception = null;
        }
        catch (FeatureFileException ex)
        {
            // The scenario holds that an unparsable file throws naming the path FeatureFiles was
            // given, so the scenario fails if the library stops naming that path; the Then step
            // then sees the path relative to the scenario's folder, as the scenario gave it.
            Assert.Equal(Path.Combine(_root, relativePath), ex.Path);
            exceptionState.Exception = new FeatureFileException(relativePath, ex.Message);
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("FeatureFiles reads the scenario titles of the directory")]
    public void WhenFeatureFilesReadsTheScenarioTitlesOfTheDirectory()
    {
        try
        {
            _directoryResults = FeatureFiles.ReadScenarioTitlesFromDirectory(_root);
            exceptionState.Exception = null;
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("FeatureFiles computes the test name")]
    public void WhenFeatureFilesComputesTheTestName() => _testName = FeatureFiles.ComputeTestName(_scenarioTitle);

    [Then("FeatureFiles returns the titles {string} and {string} in that order")]
    public void ThenFeatureFilesReturnsTheTitlesInThatOrder(string first, string second)
        => Assert.Equal([first, second], _titles);

    [Then("FeatureFiles returns the one title {string}")]
    public void ThenFeatureFilesReturnsTheOneTitle(string title) => Assert.Equal([title], _titles);

    [Then("FeatureFiles returns no titles")]
    public void ThenFeatureFilesReturnsNoTitles() => Assert.Empty(_titles);

    [Then("FeatureFiles returns {string} with the one title {string} and {string} with the one title {string}")]
    public void ThenFeatureFilesReturnsWithTheOneTitles(string firstPath, string firstTitle, string secondPath, string secondTitle)
    {
        Assert.Equal(2, _directoryResults.Count);
        Assert.Equal(firstPath, _directoryResults[0].RelativePath);
        Assert.Equal([firstTitle], _directoryResults[0].Titles);
        Assert.Equal(secondPath, _directoryResults[1].RelativePath);
        Assert.Equal([secondTitle], _directoryResults[1].Titles);
    }

    [Then("FeatureFiles reads only those two files")]
    public void ThenFeatureFilesReadsOnlyThoseTwoFiles()
    {
        Assert.Null(exceptionState.Exception);
        Assert.Equal(2, _directoryResults.Count);
    }

    [Then("FeatureFiles throws a FeatureFileException naming the path {string}")]
    public void ThenFeatureFilesThrowsAFeatureFileExceptionNamingThePath(string path)
    {
        var exception = Assert.IsType<FeatureFileException>(exceptionState.Exception);
        Assert.Equal(path, exception.Path);
    }

    [Then("the exception message holds the parser's message")]
    public void ThenTheExceptionMessageHoldsTheParsersMessage()
    {
        var exception = Assert.IsType<FeatureFileException>(exceptionState.Exception);
        try
        {
            new Parser().Parse(new StringReader(_lastContent!));
            Assert.Fail("expected Gherkin's parser to reject the content");
        }
        catch (ParserException parserException)
        {
            Assert.Contains(parserException.Message, exception.Message);
        }
    }

    [Then("FeatureFiles returns the test name {string}")]
    public void ThenFeatureFilesReturnsTheTestName(string expected) => Assert.Equal(expected, _testName);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        _lastContent = content;
    }
}
