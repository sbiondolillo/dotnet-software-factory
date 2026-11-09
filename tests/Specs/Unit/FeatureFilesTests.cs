using Core;
using Xunit;

namespace Specs.Unit;

public sealed class FeatureFilesTests : IDisposable
{
    private const string Secret = "FACTORY_GITHUB_TOKEN=s3cret";

    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("feature-files-tests-");

    private FeatureFiles Features => new(_work.FullName);

    public void Dispose() => _work.Delete(recursive: true);

    private string Write(string path, string text)
    {
        string full = Path.Combine(_work.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    [Fact]
    public void PathsGivesEachFeatureFileRelativeToTheWorkTreeInOrdinalOrder()
    {
        Write("specs/features/b.feature", "");
        Write("specs/features/a/z.feature", "");
        Write("specs/features/B.feature", "");
        Write("specs/features/notes.md", "");
        Write("specs/other.feature", "");

        Assert.Equal(["specs/features/B.feature", "specs/features/a/z.feature", "specs/features/b.feature"], Features.Paths());
    }

    [Fact]
    public void PathsGivesNoPathWhenTheWorkTreeHasNoFeatureDirectory() => Assert.Empty(Features.Paths());

    [Theory]
    [InlineData("specs", "specs")]
    [InlineData("specs/features", "specs/features")]
    [InlineData("specs/features/sub", "specs/features/sub")]
    public void PathsRefusesADirectoryThatIsASymbolicLink(string linked, string named)
    {
        Directory.CreateDirectory(Path.Combine(_work.FullName, "outside"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_work.FullName, linked))!);
        Directory.CreateDirectory(Path.Combine(_work.FullName, "specs", "features"));
        if (linked is not "specs/features/sub")
        {
            Directory.Delete(Path.Combine(_work.FullName, linked), recursive: true);
        }

        Directory.CreateSymbolicLink(Path.Combine(_work.FullName, linked), Path.Combine(_work.FullName, "outside"));

        FeatureFileException exception = Assert.Throws<FeatureFileException>(() => Features.Paths());

        Assert.Equal(named, exception.Path);
    }

    [Fact]
    public void ReadTextIsNullForAnAbsentFile()
    {
        Assert.Null(Features.ReadText("specs/features/absent.feature"));
        Assert.Empty(Features.ScenarioTitles("specs/features/absent.feature"));
        Assert.Empty(Features.ScenarioSteps("specs/features/absent.feature"));
        Assert.Empty(Features.OverIndentedDocStringLines("specs/features/absent.feature"));
    }

    [Fact]
    public void ReadTextIsNullForADirectoryAtThePath()
    {
        Directory.CreateDirectory(Path.Combine(_work.FullName, "specs", "features", "dir.feature"));

        Assert.Null(Features.ReadText("specs/features/dir.feature"));
        Assert.Empty(Features.ScenarioSteps("specs/features/dir.feature"));
    }

    [Fact]
    public void ReadTextRefusesAFileThatIsASymbolicLink()
    {
        string target = Write("environ", Secret);
        Directory.CreateDirectory(Path.Combine(_work.FullName, "specs", "features"));
        File.CreateSymbolicLink(Path.Combine(_work.FullName, "specs", "features", "leak.feature"), target);

        FeatureFileException exception = Assert.Throws<FeatureFileException>(() => Features.ReadText("specs/features/leak.feature"));

        Assert.Equal("specs/features/leak.feature", exception.Path);
        Assert.DoesNotContain("s3cret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadTextRefusesAFileUnderADirectoryThatIsASymbolicLink()
    {
        Write("outside/leak.feature", Secret);
        Directory.CreateDirectory(Path.Combine(_work.FullName, "specs"));
        Directory.CreateSymbolicLink(Path.Combine(_work.FullName, "specs", "features"), Path.Combine(_work.FullName, "outside"));

        FeatureFileException exception = Assert.Throws<FeatureFileException>(() => Features.ScenarioTitles("specs/features/leak.feature"));

        Assert.Contains("specs/features is a symbolic link", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("specs/features")]
    [InlineData("specs/other/a.feature")]
    [InlineData("specs/features/../../etc/passwd")]
    [InlineData("specs/features//a.feature")]
    [InlineData("/specs/features/a.feature")]
    [InlineData("specs/features/..\\outside.feature")]
    public void ReadTextRefusesAPathOutsideTheFeatureDirectory(string path)
    {
        Assert.False(FeatureFiles.IsFeaturePath(path));
        Assert.Throws<ArgumentException>(() => Features.ReadText(path));
    }

    [Fact]
    public void AParseErrorOfAFileHoldsThePathAndThePositionAndNoTextOfTheFile()
    {
        Write("specs/features/environ.feature", Secret);

        FeatureFileException exception = Assert.Throws<FeatureFileException>(() => Features.ScenarioTitles("specs/features/environ.feature"));

        Assert.Equal("specs/features/environ.feature", exception.Path);
        Assert.Contains("(1:1)", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteAndRemoveChangeOnlyTheNamedFile()
    {
        Features.Write("specs/features/sub/new.feature", "Feature: N\n");
        Assert.Equal("Feature: N\n", Features.ReadText("specs/features/sub/new.feature"));

        Features.Remove("specs/features/sub/new.feature");
        Assert.Null(Features.ReadText("specs/features/sub/new.feature"));
    }

    [Fact]
    public void ReadOutlineStartsASpanAtTheCommentsDirectlyAboveItsFirstTag()
    {
        const string content = "Feature: G\n\n  # note\n  @a\n\n  @b\n  Scenario: One\n    Given one\n";

        FeatureOutline outline = FeatureFiles.ReadOutline("g.feature", content);

        Assert.Equal([new ScenarioSpan("One", 3, 8)], outline.Scenarios);
    }

    [Fact]
    public void ReadOutlineEndsASpanAtItsLastLineThatIsNotBlankAndNotAComment()
    {
        const string content = "Feature: G\n\n  Scenario Outline: One <n>\n    Given <n>\n\n    Examples:\n      | n |\n      | 1 |\n  # trailing\n\n  Scenario: Two\n    Given two\n";

        FeatureOutline outline = FeatureFiles.ReadOutline("g.feature", content);

        Assert.Equal([new ScenarioSpan("One <n>", 3, 8), new ScenarioSpan("Two", 11, 12)], outline.Scenarios);
    }

    [Fact]
    public void ReadOutlineTrimsTheNameAndReadsARuleAndABackground()
    {
        const string content = "Feature: G\n\n  Background:\n    Given a\n\n  Rule: R\n\n    Example:   One  \n      Given one\n";

        FeatureOutline outline = FeatureFiles.ReadOutline("g.feature", content);

        Assert.True(outline.HasRule);
        Assert.True(outline.HasBackground);
        Assert.Equal("One", Assert.Single(outline.Scenarios).Name);
    }
}
