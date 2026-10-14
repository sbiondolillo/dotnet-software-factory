using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class StarterTemplateSteps(StarterState state)
{
    private static readonly string[] Projects =
    [
        "src/App/App.csproj",
        "src/Core/Core.csproj",
        "tests/Specs/Specs.csproj",
        "tests/Integration/Integration.csproj",
    ];

    private string _list = "";

    [Given("the template is not installed")]
    public async Task GivenTheTemplateIsNotInstalled()
        => _list = (await state.DotnetAsync("new", "list").ConfigureAwait(false)).StandardOutput;

    [Given("the template is installed")]
    public async Task GivenTheTemplateIsInstalled() => Assert.Equal(0, await InstallAsync("templates/starter").ConfigureAwait(false));

    [When("I install the template from {string}")]
    public async Task WhenIInstallTheTemplateFrom(string path) => state.InstallExit = await InstallAsync(path).ConfigureAwait(false);

    [When("I run dotnet new {string} into an empty directory")]
    public async Task WhenIRunDotnetNew(string shortName)
        => state.NewExit = (await state.DotnetAsync("new", shortName, "-o", state.Copy).ConfigureAwait(false)).ExitCode;

    [When("I run dotnet new {string} into an empty directory named {string}")]
    public async Task WhenIRunDotnetNewNamed(string shortName, string directory)
    {
        state.NameCopy(directory);
        state.NewExit = (await state.DotnetAsync("new", shortName, "-o", state.Copy).ConfigureAwait(false)).ExitCode;
    }

    [When("I run dotnet new {string} with --name {string} into an empty directory named {string}")]
    public async Task WhenIRunDotnetNewWithName(string shortName, string name, string directory)
    {
        state.NameCopy(directory);
        state.NewExit = (await state.DotnetAsync("new", shortName, "-n", name, "-o", state.Copy).ConfigureAwait(false)).ExitCode;
    }

    [Then("the install exits with the code {int}")]
    public void ThenTheInstallExits(int code) => Assert.Equal(code, state.InstallExit);

    [Then("the dotnet new command exits with the code {int}")]
    public void ThenTheDotnetNewExits(int code) => Assert.Equal(code, state.NewExit);

    [Then("the template list shows the short name {string} with the name {string}")]
    public async Task ThenTheTemplateListShows(string shortName, string name)
    {
        _list = (await state.DotnetAsync("new", "list").ConfigureAwait(false)).StandardOutput;
        StarterChecks.ListShows(_list, shortName, name);
    }

    [Then("the template list does not show the short name {string}")]
    public void ThenTheTemplateListDoesNotShow(string shortName) => StarterChecks.ListLacks(_list, shortName);

    [Then("the copy holds every file of the starter outside {string} other than {string}, each byte-equal to the starter's file")]
    public void ThenTheCopyHoldsEveryFile(string _, string except) => StarterChecks.EveryFileEqual(StarterState.Starter, state.Copy, except);

    [Then("the copy's {string} gives the property {string} the value {string}")]
    public void ThenTheCopysProjectGivesTheProperty(string project, string name, string value)
        => StarterChecks.Property(Path.Combine(state.Copy, project), name, value);

    [Then("the copy holds no other file")]
    public void ThenTheCopyHoldsNoOtherFile() => StarterChecks.NoOtherFile(StarterState.Starter, state.Copy);

    [Then("the copy's file {string} is byte-equal to the file {string} of this repo")]
    public void ThenTheCopysFileIsByteEqual(string file, string repoFile)
        => Assert.Equal(File.ReadAllBytes(Path.Combine(StarterChecks.RepoRoot(), repoFile)), File.ReadAllBytes(Path.Combine(state.Copy, file)));

    [Then("the copy holds no file under {string}")]
    public void ThenTheCopyHoldsNoFileUnder(string directory) => StarterChecks.NoFileUnder(state.Copy, directory);

    [Then("the copy's Solution.slnx names these projects")]
    public void ThenTheCopysSolutionNames(Table table)
        => StarterChecks.SolutionNames(Path.Combine(state.Copy, "Solution.slnx"), table.Rows.Select(row => row["project"]));

    [Then("the copy's specs\\/features directory holds one feature file with one scenario")]
    public void ThenTheCopysFeaturesHoldOne() => StarterChecks.OneFeatureWithOneScenario(Path.Combine(state.Copy, "specs", "features"));

    [Then("the copy's tests\\/Specs\\/Steps directory holds one step class")]
    public void ThenTheCopysStepsHoldOne() => StarterChecks.OneStepClass(Path.Combine(state.Copy, "tests", "Specs", "Steps"));

    [Then("the copy's tests\\/Integration directory holds these files and no other")]
    public void ThenTheCopysIntegrationHolds(Table table)
        => StarterChecks.HoldsOnly(Path.Combine(state.Copy, "tests", "Integration"), table.Rows.Select(row => row["file"]));

    [Then("each of the four projects of the copy has a packages.lock.json beside its project file")]
    public void ThenEachProjectHasALockFile() => StarterChecks.LockFilesBeside(state.Copy, Projects);

    [Then("the copy's .gitignore holds the lines {string}, {string}, {string} and {string}")]
    public void ThenTheCopysGitignoreHolds(string a, string b, string c, string d)
        => StarterChecks.HoldsLines(Path.Combine(state.Copy, ".gitignore"), a, b, c, d);

    [Then("the copy's {string} references the package {string} and no other package")]
    public void ThenTheCopysProjectReferencesOne(string project, string package)
        => StarterChecks.References(Path.Combine(state.Copy, project), package);

    [Then("the copy's {string} references the packages {string} and {string} and no other package")]
    public void ThenTheCopysProjectReferencesTwo(string project, string first, string second)
        => StarterChecks.References(Path.Combine(state.Copy, project), first, second);

    [Then("the copy's src\\/Core directory defines the root command and its argument name")]
    public void ThenTheCopysCoreDefinesTheRoot() => StarterChecks.DefinesRootCommandWithName(Path.Combine(state.Copy, "src", "Core"));

    [Then("the copy's src\\/App\\/Program.cs parses its arguments with that root command")]
    public void ThenTheCopysProgramParses() => StarterChecks.ParsesWithRootCommand(Path.Combine(state.Copy, "src", "App", "Program.cs"));

    [Then("the copy's Directory.Packages.props gives {string} the version {string} and {string} the version {string}")]
    public void ThenTheCopysPropsGive(string first, string firstVersion, string second, string secondVersion)
    {
        string props = Path.Combine(state.Copy, "Directory.Packages.props");
        StarterChecks.PackageVersion(props, first, firstVersion);
        StarterChecks.PackageVersion(props, second, secondVersion);
    }

    private async Task<int> InstallAsync(string path)
        => (await state.DotnetAsync("new", "install", Path.Combine(StarterChecks.RepoRoot(), path)).ConfigureAwait(false)).ExitCode;
}
