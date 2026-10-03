using Specs.Steps;
using Xunit;

namespace Specs.Unit;

public sealed class StarterChecksTests : IDisposable
{
    private readonly string _starter = Directory.CreateTempSubdirectory("factory-starter-a-").FullName;
    private readonly string _copy = Directory.CreateTempSubdirectory("factory-starter-b-").FullName;

    public void Dispose()
    {
        Directory.Delete(_starter, recursive: true);
        Directory.Delete(_copy, recursive: true);
    }

    private static void Write(string root, string path, string content)
    {
        string full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void ACopyEqualToTheStarterHasNoProblem()
    {
        Write(_starter, "a/b.txt", "x");
        Write(_starter, ".template.config/template.json", "{}");
        Write(_copy, "a/b.txt", "x");

        Assert.Empty(StarterChecks.CompareTrees(_starter, _copy));
    }

    [Fact]
    public void AMissingFileIsNamed()
    {
        Write(_starter, "a/b.txt", "x");

        Assert.Equal(["missing file a/b.txt"], StarterChecks.CompareTrees(_starter, _copy));
    }

    [Fact]
    public void AnExtraFileIsNamed()
    {
        Write(_copy, ".template.config/template.json", "{}");

        Assert.Equal(["extra file .template.config/template.json"], StarterChecks.CompareTrees(_starter, _copy));
    }

    [Fact]
    public void ADifferingFileIsNamed()
    {
        Write(_starter, "a.txt", "x");
        Write(_copy, "a.txt", "y");

        Assert.Equal(["differing file a.txt"], StarterChecks.CompareTrees(_starter, _copy));
    }

    [Fact]
    public void ThePackageCheckNamesAnUnexpectedAndAMissingPackage()
    {
        string project = Path.Combine(_copy, "P.csproj");
        File.WriteAllText(project, "<Project><ItemGroup><PackageReference Include=\"A\" /><PackageReference Include=\"C\" /></ItemGroup></Project>");

        string[] problems = [.. StarterChecks.PackageProblems(project, "A", "B")];

        Assert.Equal(2, problems.Length);
        Assert.Contains($"{project} lacks the package B", problems);
        Assert.Contains($"{project} references the package C", problems);
        Assert.Empty(StarterChecks.PackageProblems(project, "A", "C"));
    }

    [Fact]
    public void TheVersionCheckNamesAWrongVersionAndAMissingPackage()
    {
        string props = Path.Combine(_copy, "Directory.Packages.props");
        File.WriteAllText(props, "<Project><ItemGroup><PackageVersion Include=\"A\" Version=\"1.0\" /></ItemGroup></Project>");

        string[] problems = [.. StarterChecks.VersionProblems(props, new Dictionary<string, string> { ["A"] = "2.0", ["B"] = "1.0" })];

        Assert.Equal([$"{props} gives A the version 1.0, not 2.0", $"{props} gives no version to B"], problems);
        Assert.Empty(StarterChecks.VersionProblems(props, new Dictionary<string, string> { ["A"] = "1.0" }));
    }

    [Fact]
    public void TheExactFilesCheckNamesAnExtraAndAMissingFile()
    {
        Write(_copy, "Integration.csproj", "");
        Write(_copy, "Other.cs", "");

        string[] problems = [.. StarterChecks.ExactFiles(_copy, "Integration.csproj", "packages.lock.json")];

        Assert.Equal([$"{_copy} lacks packages.lock.json", $"{_copy} holds Other.cs"], problems);
    }

    [Fact]
    public void TheLockCheckNamesTheProjectWithNoLockFile()
    {
        Write(_copy, "Solution.slnx", "<Solution><Project Path=\"src/A/A.csproj\" /><Project Path=\"src/B/B.csproj\" /></Solution>");
        Write(_copy, "src/A/packages.lock.json", "{}");

        Assert.Equal(["no packages.lock.json beside src/B/B.csproj"], StarterChecks.MissingLockFiles(_copy));
    }
}
