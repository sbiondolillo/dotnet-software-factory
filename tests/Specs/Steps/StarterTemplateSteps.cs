using Core;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class StarterTemplateSteps
{
    private readonly string _root = StarterChecks.RepoRoot();
    private readonly string _home = Directory.CreateTempSubdirectory("factory-starter-home-").FullName;
    private readonly string _copy = Directory.CreateTempSubdirectory("factory-starter-copy-").FullName;
    private CommandResult? _result;
    private CommandResult? _list;

    [AfterScenario]
    public void RemoveDirectories()
    {
        Directory.Delete(_home, recursive: true);
        Directory.Delete(_copy, recursive: true);
    }

    [Given("a fresh dotnet home")]
    public void GivenAFreshDotnetHome() => Assert.Empty(Directory.GetFileSystemEntries(_home));

    [Given("the starter template is installed")]
    public async Task GivenTheStarterTemplateIsInstalled()
    {
        CommandResult result = await DotnetAsync(_root, "new", "install", Path.Combine(_root, "templates/starter"));
        Assert.Equal(0, result.ExitCode);
    }

    [When("I run dotnet new install on {string}")]
    public async Task WhenIRunDotnetNewInstallOn(string path)
    {
        _result = await DotnetAsync(_root, "new", "install", Path.Combine(_root, path));
        _list = await DotnetAsync(_root, "new", "list");
    }

    [When("I run dotnet new factory-starter into an empty directory")]
    public async Task WhenIRunDotnetNewFactoryStarterIntoAnEmptyDirectory() =>
        _result = await DotnetAsync(_copy, "new", "factory-starter");

    [Then("the command exits with the code {int}")]
    public void ThenTheCommandExitsWithTheCode(int code) => Assert.Equal(code, _result?.ExitCode);

    [Then("dotnet new list lists the short name {string} with the name {string}")]
    public void ThenDotnetNewListListsTheShortNameWithTheName(string shortName, string name)
    {
        string output = _list?.StandardOutput ?? "";
        Assert.Contains(output.Split('\n'), line => line.Contains(name, StringComparison.Ordinal) && line.Contains(shortName, StringComparison.Ordinal));
    }

    [Then("the directory holds every file of the starter outside {string}, each byte-equal to the starter's file, and no other file")]
    public void ThenTheDirectoryHoldsEveryFileOfTheStarter(string excluded)
    {
        Assert.Equal(".template.config/", excluded);
        Assert.Empty(StarterChecks.CompareTrees(Path.Combine(_root, "templates/starter"), _copy));
    }

    [Then("the copy's Solution.slnx names the four projects of the copy")]
    public void ThenTheSolutionNamesTheFourProjects() =>
        Assert.Equal(
            ["src/App/App.csproj", "src/Core/Core.csproj", "tests/Integration/Integration.csproj", "tests/Specs/Specs.csproj"],
            StarterChecks.SolutionProjects(_copy).Order(StringComparer.Ordinal).ToArray());

    [Then("the copy's specs\\/features directory holds one feature file with one scenario")]
    public void ThenTheCopyHoldsOneFeatureFile()
    {
        string[] files = Directory.GetFiles(Path.Combine(_copy, "specs/features"), "*.feature");
        string file = Assert.Single(files);
        Assert.Single(File.ReadAllLines(file), line => line.TrimStart().StartsWith("Scenario:", StringComparison.Ordinal));
    }

    [Then("the copy's tests\\/Specs\\/Steps directory holds one step class")]
    public void ThenTheCopyHoldsOneStepClass() =>
        Assert.Single(Directory.GetFiles(Path.Combine(_copy, "tests/Specs/Steps"), "*.cs"));

    [Then("the copy's tests\\/Integration directory holds {string} and {string}, and those two files only")]
    public void ThenTheIntegrationDirectoryHolds(string first, string second) =>
        Assert.Empty(StarterChecks.ExactFiles(Path.Combine(_copy, "tests/Integration"), first, second));

    [Then("each of the four projects of the copy has a packages.lock.json beside its project file")]
    public void ThenEachProjectHasALockFile()
    {
        Assert.Equal(4, StarterChecks.SolutionProjects(_copy).Count);
        Assert.Empty(StarterChecks.MissingLockFiles(_copy));
    }

    [Then("the copy's .gitignore holds the lines {string}, {string}, {string} and {string}")]
    public void ThenTheGitignoreHoldsTheLines(string first, string second, string third, string fourth)
    {
        string[] lines = File.ReadAllLines(Path.Combine(_copy, ".gitignore"));
        Assert.All([first, second, third, fourth], line => Assert.Contains(line, lines));
    }

    [Then("the copy's src\\/App\\/App.csproj references the package {string}, and no other package")]
    public void ThenTheAppProjectReferencesOnly(string package) =>
        Assert.Empty(StarterChecks.PackageProblems(Path.Combine(_copy, "src/App/App.csproj"), package));

    [Then("the copy's src\\/Core defines the root command and its argument {string}")]
    public void ThenTheCoreDefinesTheRootCommand(string argument)
    {
        string[] sources = Directory.GetFiles(Path.Combine(_copy, "src/Core"), "*.cs").Select(File.ReadAllText).ToArray();
        Assert.Contains(sources, text => text.Contains("new RootCommand", StringComparison.Ordinal) && text.Contains($"\"{argument}\"", StringComparison.Ordinal));
    }

    [Then("the copy's src\\/App\\/Program.cs parses its arguments with that root command")]
    public void ThenTheProgramParsesWithTheRootCommand()
    {
        string program = File.ReadAllText(Path.Combine(_copy, "src/App/Program.cs"));
        Assert.Contains("RootCommand root = ", program, StringComparison.Ordinal);
        Assert.Contains("root.Parse(args)", program, StringComparison.Ordinal);
    }

    [Then("the copy's src\\/Core\\/Core.csproj references the packages {string} and {string}, and no other package")]
    public void ThenTheCoreProjectReferencesOnly(string first, string second) =>
        Assert.Empty(StarterChecks.PackageProblems(Path.Combine(_copy, "src/Core/Core.csproj"), first, second));

    [Then("the copy's Directory.Packages.props gives {string} the version {string} and {string} the version {string}")]
    public void ThenThePropsGiveTheVersions(string first, string firstVersion, string second, string secondVersion) =>
        Assert.Empty(StarterChecks.VersionProblems(
            Path.Combine(_copy, "Directory.Packages.props"),
            new Dictionary<string, string> { [first] = firstVersion, [second] = secondVersion }));

    private Task<CommandResult> DotnetAsync(string directory, params string[] arguments) =>
        new CommandRunner().RunAsync(
            new CommandSpec
            {
                Command = "dotnet",
                Arguments = arguments,
                WorkingDirectory = directory,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["DOTNET_CLI_HOME"] = _home,
                    ["DOTNET_NOLOGO"] = "1",
                    ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                },
            },
            TestContext.Current.CancellationToken);
}
