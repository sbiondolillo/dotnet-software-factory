using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeatureFilesTests
{
    [Fact]
    public void ReadScenarioTitlesThrowsAFeatureFileExceptionNamingThePathAndHoldingTheParserSMessage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.feature");
        File.WriteAllText(path, "not valid gherkin at all @#$%");

        Exception parserException = Assert.ThrowsAny<Exception>(
            () => new Gherkin.Parser().Parse(new StringReader(File.ReadAllText(path))));

        FeatureFileException exception = Assert.Throws<FeatureFileException>(() => FeatureFiles.ReadScenarioTitles(path));

        Assert.Contains(path, exception.Message);
        Assert.Contains(parserException.Message, exception.Message);
    }
}
