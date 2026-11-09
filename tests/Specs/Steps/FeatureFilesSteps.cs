using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class FeatureFilesSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("feature-files-").FullName;

    // Each file of a scenario sits in the feature directory of the work tree _root, and a scenario names it relative to that directory.
    private string Features => Path.Combine(_root, FeatureFiles.RelativePath);

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

    [Given("{string} is a symbolic link to the file {string}")]
    public void GivenASymbolicLink(string fileName, string target) =>
        File.CreateSymbolicLink(Path.Combine(Features, fileName), Path.Combine(Features, target));

    [Given("the scenario title {string}")]
    public void GivenTheScenarioTitle(string title) => _scenarioTitle = title;

    [When("FeatureFiles reads the scenario titles of {string}")]
    public void WhenFeatureFilesReadsTheScenarioTitlesOf(string relativePath)
    {
        try
        {
            _titles = new FeatureFiles(_root).ScenarioTitles(FeatureFiles.RelativePath + relativePath);
            exceptionState.Exception = null;
        }
        catch (FeatureFileException ex)
        {
            // The scenario holds that an unparsable file throws naming the path FeatureFiles was
            // given, so the scenario fails if the library stops naming that path; the Then step
            // then sees the path relative to the feature directory, as the scenario gave it.
            Assert.Equal(FeatureFiles.RelativePath + relativePath, ex.Path);
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
            var features = new FeatureFiles(_root);
            _directoryResults = [.. features.Paths().Select(path => (path[FeatureFiles.RelativePath.Length..], features.ScenarioTitles(path)))];
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

    [Then("FeatureFiles throws a FeatureFileException that holds {string} and lacks {string}")]
    public void ThenFeatureFilesThrowsAFeatureFileExceptionThatHoldsAndLacks(string held, string lacked)
    {
        FeatureFileException exception = Assert.IsType<FeatureFileException>(exceptionState.Exception);
        Assert.Contains(held, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(lacked, exception.Message, StringComparison.Ordinal);
    }

    [Then("FeatureFiles throws a FeatureFileException naming the path {string}")]
    public void ThenFeatureFilesThrowsAFeatureFileExceptionNamingThePath(string path)
    {
        FeatureFileException exception = Assert.IsType<FeatureFileException>(exceptionState.Exception);
        Assert.Equal(path, exception.Path);
    }

    [Then("FeatureFiles returns the test name {string}")]
    public void ThenFeatureFilesReturnsTheTestName(string expected) => Assert.Equal(expected, _testName);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(Features, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
