using Core;
using Xunit;

namespace Specs.Unit;

public sealed class CleanupBodyTests
{
    [Fact]
    public void NoScopeHeadingGivesNoEntry() => Assert.Empty(CleanupBody.ReadScope("## Check\n\n```sh\ntrue\n```\n"));

    [Fact]
    public void ABlankScopeGivesNoEntry() => Assert.Empty(CleanupBody.ReadScope("## Scope\n\n  \n## Check\n"));

    [Fact]
    public void AScopeAsTheLastHeadingReadsToTheEnd() => Assert.Equal(["a.md"], CleanupBody.ReadScope("## Check\n\n## Scope\na.md\n"));

    [Fact]
    public void TheEntryKindsReadAsWritten() =>
        Assert.Equal(["tests/", "docs/*.md", "README.md", "src/a.cs"], CleanupBody.ReadScope("## Scope\ntests/\n- `docs/*.md`\n\nREADME.md\n* `src/a.cs`\n"));

    [Fact]
    public void NoCheckHeadingGivesNoCommand() => Assert.Null(CleanupBody.ReadCheck("## Scope\na\n"));

    [Fact]
    public void ACheckWithNoFenceBeforeTheNextHeadingGivesNoCommand() =>
        Assert.Null(CleanupBody.ReadCheck("## Check\ntext\n\n## Notes\n```sh\ntrue\n```\n"));

    [Fact]
    public void AnUnclosedFenceGivesNoCommand() => Assert.Null(CleanupBody.ReadCheck("## Check\n```sh\ntrue\n"));

    [Fact]
    public void ABlankBlockGivesNoCommand() => Assert.Null(CleanupBody.ReadCheck("## Check\n```sh\n\n```\n"));

    [Fact]
    public void AHeadingInsideTheBlockIsText() =>
        Assert.Equal("echo a\n## not a heading\ntrue", CleanupBody.ReadCheck("## Check\n```sh\necho a\n## not a heading\ntrue\n```\n## Next\n```sh\nno\n```\n"));

    [Fact]
    public void TheFirstBlockWins() => Assert.Equal("one", CleanupBody.ReadCheck("## Check\n```sh\none\n```\n```sh\ntwo\n```\n"));

    [Theory]
    [InlineData("tests/", "tests/Specs/A.cs", true)]
    [InlineData("tests/", "src/tests/A.cs", false)]
    [InlineData("tests/Specs/Steps/*Steps.cs", "tests/Specs/Steps/GreetingSteps.cs", true)]
    [InlineData("tests/Specs/Steps/*Steps.cs", "tests/Specs/Steps/Sub/GreetingSteps.cs", false)]
    [InlineData("**/*.md", "docs/a/b.md", true)]
    [InlineData("README.md", "README.md", true)]
    [InlineData("README.md", "docs/README.md", false)]
    public void AnEntryCoversWhatItNames(string entry, string path, bool covered) =>
        Assert.Equal(covered, CleanupBody.InScope([entry], path));
}
