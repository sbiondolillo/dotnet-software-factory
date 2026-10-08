using System.CommandLine;
using Core;
using Xunit;

namespace Specs.Unit;

public sealed class StarterChecksTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("starter-checks-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private string Write(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private (string Starter, string Copy) Pair()
    {
        Write("s/a.txt", "a");
        Write("s/d/b.txt", "b");
        Write("s/.template.config/template.json", "{}");
        Write("c/a.txt", "a");
        Write("c/d/b.txt", "b");
        return (Path.Combine(_root, "s"), Path.Combine(_root, "c"));
    }

    [Fact]
    public void AListThatHoldsTheShortNameFailsTheLackCheck()
    {
        Assert.Throws<InvalidOperationException>(() => StarterChecks.ListLacks("x  starter  factory-starter  [C#]\n", "factory-starter"));
        StarterChecks.ListLacks("x  starter  other  [C#]\n", "factory-starter");
    }

    [Fact]
    public void AListWithoutTheNameFailsTheShowCheck()
    {
        StarterChecks.ListShows("dotnet-software-factory starter   factory-starter   [C#]\n", "factory-starter", "dotnet-software-factory starter");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.ListShows("other   o\n", "factory-starter", "dotnet-software-factory starter"));
    }

    [Fact]
    public void ACopyThatMatchesPasses()
    {
        (string s, string c) = Pair();
        StarterChecks.EveryFileEqual(s, c);
        StarterChecks.NoOtherFile(s, c);
        StarterChecks.NoFileUnder(c, ".template.config/");
    }

    [Fact]
    public void ACopyThatLacksAFileFails()
    {
        (string s, string c) = Pair();
        File.Delete(Path.Combine(c, "a.txt"));
        Assert.Throws<InvalidOperationException>(() => StarterChecks.EveryFileEqual(s, c));
    }

    [Fact]
    public void ACopyWithOneFileMoreFails()
    {
        (string s, string c) = Pair();
        Write("c/more.txt", "");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.NoOtherFile(s, c));
    }

    [Fact]
    public void ACopyWithOneByteDifferentFails()
    {
        (string s, string c) = Pair();
        Write("c/a.txt", "A");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.EveryFileEqual(s, c));
    }

    [Fact]
    public void ACopyWithAFileUnderTheTemplateConfigurationFails()
    {
        (_, string c) = Pair();
        Write("c/.template.config/template.json", "{}");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.NoFileUnder(c, ".template.config/"));
    }

    [Fact]
    public void TheCheckOfThePackagesOfAProjectFileIsExact()
    {
        string project = Write("p.csproj", "<Project><ItemGroup><PackageReference Include=\"A\" /><PackageReference Include=\"B\" /></ItemGroup></Project>");
        StarterChecks.References(project, "B", "A");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.References(project, "A"));
        Assert.Throws<InvalidOperationException>(() => StarterChecks.References(project, "A", "B", "C"));
    }

    [Fact]
    public void TheCheckOfTheSolutionFailsForAMissingAndAFifthProject()
    {
        string[] four = ["a.csproj", "b.csproj", "c.csproj", "d.csproj"];
        string sln = Write("s.slnx", "<Solution>" + string.Concat(four.Select(p => $"<Project Path=\"{p}\" />")) + "</Solution>");
        StarterChecks.SolutionNames(sln, four);
        Assert.Throws<InvalidOperationException>(() => StarterChecks.SolutionNames(sln, four[..3]));
        Assert.Throws<InvalidOperationException>(() => StarterChecks.SolutionNames(sln, [.. four, "e.csproj"]));
    }

    [Fact]
    public void TheCheckOfAPackageVersionFailsForAWrongVersionAndAMissingPackage()
    {
        string props = Write("p.props", "<Project><ItemGroup><PackageVersion Include=\"A\" Version=\"1.0\" /></ItemGroup></Project>");
        StarterChecks.PackageVersion(props, "A", "1.0");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.PackageVersion(props, "A", "2.0"));
        Assert.Throws<InvalidOperationException>(() => StarterChecks.PackageVersion(props, "B", "1.0"));
    }

    [Fact]
    public void TheCheckOfTheIntegrationDirectoryFailsForAFurtherFileAndAMissingLockFile()
    {
        Write("i/Integration.csproj", "");
        Write("i/packages.lock.json", "");
        string dir = Path.Combine(_root, "i");
        string[] want = ["Integration.csproj", "packages.lock.json"];
        StarterChecks.HoldsOnly(dir, want);
        Assert.Throws<InvalidOperationException>(() => StarterChecks.HoldsOnly(dir, want[..1]));
        File.Delete(Path.Combine(dir, "packages.lock.json"));
        Assert.Throws<InvalidOperationException>(() => StarterChecks.HoldsOnly(dir, want));
        Write("i/Extra.cs", "");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.HoldsOnly(dir, want));
    }

    [Fact]
    public void TheCheckOfTheLockFilesFailsWhenAProjectHasNone()
    {
        Write("a/a.csproj", "");
        Write("a/packages.lock.json", "");
        Write("b/b.csproj", "");
        StarterChecks.LockFilesBeside(_root, ["a/a.csproj"]);
        Assert.Throws<InvalidOperationException>(() => StarterChecks.LockFilesBeside(_root, ["a/a.csproj", "b/b.csproj"]));
    }

    [Fact]
    public void TheCheckOfTheGitignoreFailsForAMissingLine()
    {
        string file = Write(".gitignore", ".factory/\nbin/\nobj/\n");
        StarterChecks.HoldsLines(file, ".factory/", "bin/", "obj/");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.HoldsLines(file, ".factory/", "bin/", "obj/", "TestResults/"));
    }

    [Fact]
    public void TheCheckOfTheFeatureDirectoryFailsForTwoFeaturesAndTwoScenarios()
    {
        Write("f/a.feature", "Feature: A\n  Scenario: one\n    Given x\n");
        StarterChecks.OneFeatureWithOneScenario(Path.Combine(_root, "f"));
        Write("f/a.feature", "Feature: A\n  Scenario: one\n  Scenario: two\n");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.OneFeatureWithOneScenario(Path.Combine(_root, "f")));
        Write("f/a.feature", "Feature: A\n  Scenario: one\n");
        Write("f/b.feature", "Feature: B\n  Scenario: one\n");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.OneFeatureWithOneScenario(Path.Combine(_root, "f")));
    }

    [Fact]
    public void TheCheckOfTheStepsDirectoryFailsForTwoStepClasses()
    {
        Write("st/A.cs", "");
        StarterChecks.OneStepClass(Path.Combine(_root, "st"));
        Write("st/B.cs", "");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.OneStepClass(Path.Combine(_root, "st")));
    }

    [Fact]
    public void TheRootCommandChecksFailForAnEmptyCoreAndAProgramWithoutIt()
    {
        Write("core/X.cs", "class X {}");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.DefinesRootCommandWithName(Path.Combine(_root, "core")));
        string program = Write("Program.cs", "return 0;");
        Assert.Throws<InvalidOperationException>(() => StarterChecks.ParsesWithRootCommand(program));
    }

    [Theory]
    [InlineData("Ada", "Hello, Ada!\n")]
    [InlineData("", "Hello, world!\n")]
    public async Task TheStarterRootCommandGreets(string name, string expected)
    {
        var output = new StringWriter { NewLine = "\n" };
        var config = new InvocationConfiguration { Output = output, Error = new StringWriter() };
        string[] args = name.Length is 0 ? [] : [name];
        int exit = await GreetCommand.Create().Parse(args).InvokeAsync(config, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(0, exit);
        Assert.Equal(expected, output.ToString());
    }

    [Fact]
    public async Task AnExtraArgumentGivesAParseErrorAndANonZeroExitCode()
    {
        var error = new StringWriter();
        var config = new InvocationConfiguration { Output = new StringWriter(), Error = error };
        int exit = await GreetCommand.Create().Parse(["Ada", "Bob"]).InvokeAsync(config, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.NotEqual(0, exit);
        Assert.NotEqual("", error.ToString());
    }

    [Fact]
    public void TextEqualsFailsOnMissingFile()
    {
        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => StarterChecks.TextEquals(Path.Combine(_root, "AGENTS.md"), "a"));
        Assert.Contains("AGENTS.md", e.Message);
    }

    [Fact]
    public void TextEqualsShowsFirstDifferingLine()
    {
        string path = Write("t.md", "one\ntwx\nthree\n");
        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => StarterChecks.TextEquals(path, "one\ntwo\nthree"));
        Assert.Contains("line 2", e.Message);
        Assert.Contains("twx", e.Message);
    }

    [Fact]
    public void TextEqualsFailsOnExtraLine()
        => Assert.Throws<InvalidOperationException>(() => StarterChecks.TextEquals(Write("t.md", "one\ntwo\nextra\n"), "one\ntwo"));

    [Theory]
    [InlineData("one\ntwo")]
    [InlineData("one\ntwo\n")]
    public void TextEqualsPassesWithOrWithoutFinalLineFeed(string content)
        => StarterChecks.TextEquals(Write("t.md", content), "one\ntwo\n");

    private string Project(string line, string more = "") => Write("p.csproj", $"<Project>\n  <!-- {line}\n       {more}\n  -->\n</Project>\n");

    [Fact]
    public void CommentStartsFailsOnOtherLineAndShowsBoth()
    {
        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => StarterChecks.CommentStarts(Project("old"), "new"));
        Assert.Contains("old", e.Message);
        Assert.Contains("new", e.Message);
    }

    [Fact]
    public void CommentStartsFailsOnNoCommentOrMissingFile()
    {
        string plain = Write("q.csproj", "<Project />");
        Assert.Contains("q.csproj", Assert.Throws<InvalidOperationException>(() => StarterChecks.CommentStarts(plain, "x")).Message);
        Assert.Contains("nope.csproj", Assert.Throws<InvalidOperationException>(() => StarterChecks.CommentStarts(Path.Combine(_root, "nope.csproj"), "x")).Message);
    }

    [Fact]
    public void CommentStartsPasses() => StarterChecks.CommentStarts(Project("new"), "new");

    [Theory]
    [InlineData("target", "")]
    [InlineData("first", "target")]
    public void CommentLacksFailsWhenAnyLineIsTheLine(string first, string second)
    {
        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => StarterChecks.CommentLacks(Project(first, second), "target"));
        Assert.Contains("target", e.Message);
    }

    [Fact]
    public void CommentLacksPassesForALongerLine() => StarterChecks.CommentLacks(Project("a target here"), "target");
}
