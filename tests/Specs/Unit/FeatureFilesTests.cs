using Core;
using Gherkin;
using Xunit;

namespace Specs.Unit;

public sealed class FeatureFilesTests
{
    [Fact]
    public void ReadScenarioTitlesThrowsAFeatureFileExceptionNamingTheFileWhenTheParserRejectsItsContent()
    {
        string directory = Directory.CreateTempSubdirectory("feature-files-tests-").FullName;
        try
        {
            string path = Path.Combine(directory, "invalid.feature");
            File.WriteAllText(path, "This is not a feature file.");

            FeatureFileException exception = Assert.Throws<FeatureFileException>(() => FeatureFiles.ReadScenarioTitles(path));

            Assert.Equal(path, exception.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadScenarioTitlesExceptionMessageHoldsTheParsersOwnMessage()
    {
        string directory = Directory.CreateTempSubdirectory("feature-files-tests-").FullName;
        try
        {
            string path = Path.Combine(directory, "invalid.feature");
            const string content = "This is not a feature file.";
            File.WriteAllText(path, content);

            FeatureFileException exception = Assert.Throws<FeatureFileException>(() => FeatureFiles.ReadScenarioTitles(path));

            string parserMessage = Assert.ThrowsAny<ParserException>(() => new Parser().Parse(new StringReader(content))).Message;
            Assert.Contains(parserMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
