using System.Text.Json;
using Xunit;

namespace Specs.Unit;

public sealed class StarterTemplateTests
{
    private static string Starter => StarterChecks.StarterFolder();

    private static string Repo => Path.GetFullPath(Path.Combine(Starter, "..", ".."));

    [Fact]
    public void TheTemplateConfigNamesTheTemplateAndKeepsLockFiles()
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Starter, ".template.config", "template.json")));
        JsonElement root = json.RootElement;
        Assert.Equal("dotnet-software-factory starter", root.GetProperty("name").GetString());
        Assert.Equal("factory-starter", root.GetProperty("shortName").GetString());
        string?[] excluded = [.. root.GetProperty("sources")[0].GetProperty("exclude").EnumerateArray().Select(e => e.GetString())];
        Assert.Contains(".template.config/**", excluded);
        Assert.Contains("**/bin/**", excluded);
        Assert.Contains("**/obj/**", excluded);
        Assert.DoesNotContain("**/*.lock.json", excluded);
        Assert.DoesNotContain(excluded, e => e!.Contains("lock", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("global.json", "global.json")]
    [InlineData("Directory.Build.props", "Directory.Build.props")]
    [InlineData(".editorconfig", ".editorconfig")]
    [InlineData("tests/Specs/Specs.csproj", "tests/Specs/Specs.csproj")]
    [InlineData("tests/Specs/reqnroll.json", "tests/Specs/reqnroll.json")]
    [InlineData("tests/Integration/Integration.csproj", "tests/Integration/Integration.csproj")]
    [InlineData("src/Core/Greeter.cs", "src/Core/Greeter.cs")]
    [InlineData("specs/features/greeting.feature", "specs/features/greeting.feature")]
    [InlineData("tests/Specs/Steps/GreetingSteps.cs", "tests/Specs/Steps/GreetingSteps.cs")]
    public void AStarterFileIsByteEqualToTheRepos(string starterPath, string repoPath) =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(Repo, repoPath)), File.ReadAllBytes(Path.Combine(Starter, starterPath)));

    [Fact]
    public void DifferencesReportAMissingAnExtraAndAChangedFile()
    {
        string root = Directory.CreateTempSubdirectory("starter-checks-").FullName;
        try
        {
            string a = Path.Combine(root, "a");
            string b = Path.Combine(root, "b");
            Directory.CreateDirectory(Path.Combine(a, ".template.config"));
            Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(a, ".template.config", "template.json"), "{}");
            File.WriteAllText(Path.Combine(a, "missing.txt"), "x");
            File.WriteAllText(Path.Combine(a, "same.txt"), "x");
            File.WriteAllText(Path.Combine(b, "same.txt"), "y");
            File.WriteAllText(Path.Combine(b, "extra.txt"), "x");

            Assert.Equal(["missing: missing.txt", "extra: extra.txt", "differs: same.txt"], StarterChecks.Differences(a, b));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageReferencesListsEveryReferenceSoAnExtraOneIsSeen()
    {
        string root = Directory.CreateTempSubdirectory("starter-checks-").FullName;
        try
        {
            string project = Path.Combine(root, "A.csproj");
            File.WriteAllText(project, "<Project><ItemGroup><PackageReference Include=\"B\" /><PackageReference Include=\"A\" /></ItemGroup></Project>");
            Assert.Equal(["A", "B"], StarterChecks.PackageReferences(project));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheStarterManifestPinsTheTwoVersionsAndHasNoGherkin()
    {
        IReadOnlyDictionary<string, string> versions = StarterChecks.PackageVersions(Path.Combine(Starter, "Directory.Packages.props"));
        Assert.Equal("10.0.12", versions["Microsoft.Extensions.Hosting"]);
        Assert.Equal("2.0.12", versions["System.CommandLine"]);
        Assert.DoesNotContain("Gherkin", versions.Keys);
        Assert.DoesNotContain("Reqnroll", versions.Keys);
    }
}
