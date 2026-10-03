using System.Diagnostics;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class StarterTemplateSteps : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("starter-template-").FullName;
    private readonly string _home;
    private readonly string _copy;
    private int _exitCode = -1;
    private string _output = "";

    public StarterTemplateSteps()
    {
        _home = Path.Combine(_root, "home");
        _copy = Path.Combine(_root, "copy");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_copy);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Given("a fresh dotnet home with no template installed")]
    public void GivenAFreshDotnetHome()
    {
        Assert.Empty(Directory.EnumerateFileSystemEntries(_home));
    }

    [Given("the starter template is installed")]
    public void GivenTheStarterTemplateIsInstalled()
    {
        Install(StarterChecks.StarterFolder());
        Assert.Equal(0, _exitCode);
    }

    [Given("I install the template from {string}")]
    [When("I install the template from {string}")]
    public void WhenIInstallTheTemplateFrom(string folder) =>
        Install(Path.GetFullPath(Path.Combine(StarterChecks.StarterFolder(), "..", "..", folder)));

    [When("I create a copy named {string} in an empty directory")]
    public void WhenICreateACopyNamed(string shortName) => Dotnet("new", shortName, "-o", _copy);

    [Then("the install exits with the code {int}")]
    public void ThenTheInstallExitsWithTheCode(int code) => Assert.Equal(code, _exitCode);

    [Then("the creation exits with the code {int}")]
    public void ThenTheCreationExitsWithTheCode(int code) => Assert.Equal(code, _exitCode);

    [Then("the template list shows the short name {string} with the name {string}")]
    public void ThenTheTemplateListShows(string shortName, string name)
    {
        Dotnet("new", "list");
        Assert.Equal(0, _exitCode);
        Assert.Contains(_output.Split('\n'), line => line.Contains(name, StringComparison.Ordinal) && line.Contains(shortName, StringComparison.Ordinal));
    }

    [Then("the copy is not written")]
    public void ThenTheCopyIsNotWritten() => Assert.Empty(Directory.EnumerateFileSystemEntries(_copy));

    [Then("the copy holds every file of the starter outside {string}, each byte-equal to the starter's file, and no other file")]
    public void ThenTheCopyHoldsEveryFile(string _) =>
        Assert.Empty(StarterChecks.Differences(StarterChecks.StarterFolder(), _copy));

    [Then("the copy's Solution.slnx names these projects")]
    public void ThenTheSolutionNames(Table table) =>
        Assert.Equal(
            table.Rows.Select(row => row["project"]).Order(StringComparer.Ordinal),
            StarterChecks.Projects(Path.Combine(_copy, "Solution.slnx")));

    [Then("the copy's specs\\/features directory holds one feature file with one scenario")]
    public void ThenTheCopyHoldsOneFeature()
    {
        string[] files = Directory.GetFiles(Path.Combine(_copy, "specs", "features"));
        string file = Assert.Single(files);
        Assert.EndsWith(".feature", file, StringComparison.Ordinal);
        Assert.Single(File.ReadAllLines(file), line => line.TrimStart().StartsWith("Scenario:", StringComparison.Ordinal));
    }

    [Then("the copy's tests\\/Specs\\/Steps directory holds one step class")]
    public void ThenTheCopyHoldsOneStepClass()
    {
        string file = Assert.Single(Directory.GetFiles(Path.Combine(_copy, "tests", "Specs", "Steps")));
        Assert.EndsWith(".cs", file, StringComparison.Ordinal);
        Assert.Contains("[Binding]", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Then("the copy's tests\\/Integration directory holds Integration.csproj and packages.lock.json, and those two files only")]
    public void ThenTheIntegrationDirectoryHoldsTwoFiles() =>
        Assert.Equal(
            ["Integration.csproj", "packages.lock.json"],
            Directory.GetFileSystemEntries(Path.Combine(_copy, "tests", "Integration")).Select(Path.GetFileName).Order(StringComparer.Ordinal));

    [Then("each of the four projects of the copy has a packages.lock.json beside its project file")]
    public void ThenEachProjectHasALockFile()
    {
        IReadOnlyList<string> projects = StarterChecks.Projects(Path.Combine(_copy, "Solution.slnx"));
        Assert.Equal(4, projects.Count);
        foreach (string project in projects)
        {
            Assert.True(File.Exists(Path.Combine(_copy, Path.GetDirectoryName(project)!, "packages.lock.json")), project);
        }
    }

    [Then("the copy's .gitignore holds the lines {string}, {string}, {string} and {string}")]
    public void ThenTheGitignoreHoldsTheLines(string a, string b, string c, string d)
    {
        string[] lines = File.ReadAllLines(Path.Combine(_copy, ".gitignore"));
        foreach (string line in new[] { a, b, c, d })
        {
            Assert.Contains(line, lines);
        }
    }

    [Then("the copy's src\\/App\\/App.csproj references the package {string} and no other package")]
    public void ThenAppReferencesOnly(string package) =>
        Assert.Equal([package], StarterChecks.PackageReferences(Path.Combine(_copy, "src", "App", "App.csproj")));

    [Then("the copy's src\\/Core defines the root command and its argument {string}")]
    public void ThenCoreDefinesTheRootCommand(string argument)
    {
        string code = string.Concat(Directory.GetFiles(Path.Combine(_copy, "src", "Core"), "*.cs").Select(File.ReadAllText));
        Assert.Contains("new RootCommand(", code, StringComparison.Ordinal);
        Assert.Contains($"Argument<string>(\"{argument}\")", code, StringComparison.Ordinal);
    }

    [Then("the copy's src\\/App\\/Program.cs parses its arguments with that root command")]
    public void ThenProgramParsesWithTheRoot()
    {
        string code = File.ReadAllText(Path.Combine(_copy, "src", "App", "Program.cs"));
        Assert.Contains("RootCommand root = StarterCommands.Create(", code, StringComparison.Ordinal);
        Assert.Contains("root.Parse(args)", code, StringComparison.Ordinal);
    }

    [Then("the copy's src\\/Core\\/Core.csproj references the packages {string} and {string}, and no other package")]
    public void ThenCoreReferencesOnly(string first, string second) =>
        Assert.Equal(
            new[] { first, second }.Order(StringComparer.Ordinal),
            StarterChecks.PackageReferences(Path.Combine(_copy, "src", "Core", "Core.csproj")));

    [Then("the copy's Directory.Packages.props gives {string} the version {string} and {string} the version {string}")]
    public void ThenThePropsGiveTheVersions(string first, string firstVersion, string second, string secondVersion)
    {
        IReadOnlyDictionary<string, string> versions = StarterChecks.PackageVersions(Path.Combine(_copy, "Directory.Packages.props"));
        Assert.Equal(firstVersion, versions[first]);
        Assert.Equal(secondVersion, versions[second]);
    }

    private void Install(string folder) => Dotnet("new", "install", folder);

    private void Dotnet(params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["DOTNET_CLI_HOME"] = _home;
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using Process process = Process.Start(info)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _output = output + error.GetAwaiter().GetResult();
        _exitCode = process.ExitCode;
    }
}
