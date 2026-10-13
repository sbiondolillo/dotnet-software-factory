using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class SandboxSteps(ExceptionState exceptionState, ScenarioContext scenarioContext, WorkTreeState workTree) : IDisposable
{
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string HomeSuffix = ":/tmp/sandbox-home";
    private readonly List<CommandSpec> _recorded = [];
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-sandbox-steps-");
    private CommandResult _run = new() { StandardOutput = ContainerId + "\n" };
    private CommandResult _exec = new();
    private Sandbox? _sandbox;
    private CommandResult? _result;
    private Mock<CommandRunner>? _mock;
    private HashSet<string> _directoriesAtRun = [];
    private readonly HashSet<string> _homesAtRun = [];
    private string? _fileExecWrites;

    public void Dispose() => _directory.Delete(recursive: true);

    private CommandRunner Runner()
    {
        if (_mock is null)
        {
            _mock = new Mock<CommandRunner>(MockBehavior.Strict);
            _mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
                .Returns((CommandSpec spec, CancellationToken _) =>
                {
                    _recorded.Add(spec);
                    if (spec.Arguments[0] is "run" && workTree.InUse)
                    {
                        _directoriesAtRun = [.. Directory.EnumerateDirectories(workTree.Path, "*", SearchOption.AllDirectories)
                            .Select(directory => Path.GetRelativePath(workTree.Path, directory))];
                    }

                    string mountedHome = spec.Arguments[0] is "run" ? MountedHome(spec) : "";
                    if (Directory.Exists(mountedHome))
                    {
                        _homesAtRun.Add(mountedHome);
                    }

                    if (spec.Arguments[0] is "exec" && _fileExecWrites is not null)
                    {
                        string file = Path.Combine(MountedHome(Docker("run")), _fileExecWrites);
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        File.WriteAllText(file, "{}\n");
                    }

                    return Task.FromResult(spec.Arguments[0] switch
                    {
                        "image" => new CommandResult { ExitCode = scenarioContext.TryGetValue("inspectExit", out int code) ? code : 0 },
                        "run" => _run,
                        "exec" => _exec,
                        _ => new CommandResult(),
                    });
                });
        }

        return _mock.Object;
    }

    private async Task StartAsync()
    {
        _sandbox = await Sandbox.StartAsync(
            Runner(), "factory-sandbox:test", _directory.FullName, "/tmp/factory-work-tree", TestContext.Current.CancellationToken);
    }

    private CommandSpec Docker(string subcommand) => _recorded.Last(spec => spec.Arguments[0] == subcommand);

    private static string MountedHome(CommandSpec run)
    {
        List<string> arguments = [.. run.Arguments];
        int index = arguments.FindIndex(argument => argument.EndsWith(HomeSuffix, StringComparison.Ordinal));
        return index > 0 && arguments[index - 1] is "--volume" ? arguments[index][..^HomeSuffix.Length] : "";
    }

    private static void AssertFollows(CommandSpec spec, string flag, string value)
    {
        List<string> arguments = [.. spec.Arguments];
        Assert.Contains(arguments.Select((argument, index) => (argument, index)),
            pair => pair.argument == flag && pair.index + 1 < arguments.Count && arguments[pair.index + 1] == value);
    }

    [Given("a Sandbox built over a mock of CommandRunner")]
    public void GivenASandbox() => File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), """{"sdk":{"version":"10.0.100"}}""");

    [Given("the root directory holds a global.json whose sdk.version is {string}")]
    public void GivenGlobalJson(string version) =>
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), $"{{\"sdk\":{{\"version\":\"{version}\"}}}}");

    [Given("docker run exits with a nonzero code and writes a reason to standard error")]
    public void GivenRunFails() => _run = new CommandResult { ExitCode = 125, StandardError = "docker: no such image" };

    [Given("docker run prints a container id of 64 hexadecimal digits and a line feed")]
    public void GivenRunPrints() => _run = new CommandResult { StandardOutput = ContainerId + "\n" };

    [Given("a caller has started a sandbox")]
    public async Task GivenStarted() => await StartAsync();

    [Given("docker exec exits with the code {int}, writes {string} to standard output and writes {string} to standard error")]
    public void GivenExec(int code, string output, string error) =>
        _exec = new CommandResult { ExitCode = code, StandardOutput = output, StandardError = error };

    [Given("docker exec writes the file {string} into the mounted home")]
    public void GivenExecWrites(string relativePath) => _fileExecWrites = relativePath;

    [When("a caller starts a sandbox with the image tag {string} and the work tree {string}")]
    public async Task WhenStarts(string tag, string workTree)
    {
        exceptionState.Exception = null;
        try
        {
            _sandbox = await Sandbox.StartAsync(
                Runner(), tag, _directory.FullName, workTree, TestContext.Current.CancellationToken);
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [When("a caller starts a sandbox with the image tag {string} over that work tree")]
    public Task WhenStartsOverWorkTree(string tag) => WhenStarts(tag, workTree.Path);

    [When("a caller runs {string} with the argument {string} and an empty list of names")]
    public async Task WhenRunsWithArgument(string program, string argument) =>
        _result = await _sandbox!.RunAsync(program, [argument], [], TestContext.Current.CancellationToken);

    [When("a caller runs {string} with the names {string} and {string}")]
    public async Task WhenRunsWithNames(string program, string first, string second) =>
        _result = await _sandbox!.RunAsync(program, [], [first, second], TestContext.Current.CancellationToken);

    [When("a caller runs {string} with the name {string}")]
    public async Task WhenRunsWithName(string program, string name) =>
        _result = await _sandbox!.RunAsync(program, [], [name], TestContext.Current.CancellationToken);

    [When("a caller runs {string} with an empty list of names")]
    public async Task WhenRunsWithNoNames(string program) =>
        _result = await _sandbox!.RunAsync(program, [], [], TestContext.Current.CancellationToken);

    [When("a caller disposes the sandbox")]
    public async Task WhenDisposes() => await _sandbox!.DisposeAsync();

    [Then("the runner ran a docker build whose arguments hold {string} and then {string}")]
    public void ThenBuild(string flag, string value)
    {
        CommandSpec build = Docker("build");
        Assert.Equal("docker", build.Command);
        AssertFollows(build, flag, value);
    }

    [Then("the docker build ran before the docker run")]
    public void ThenBuildBeforeRun() =>
        Assert.True(_recorded.FindIndex(s => s.Arguments[0] == "build") < _recorded.FindIndex(s => s.Arguments[0] == "run"));

    [Then("the arguments of the docker run hold {string} and then the user id and the group id of the current process joined by a colon")]
    public void ThenUser(string flag)
    {
        string Field(string name) => File.ReadLines("/proc/self/status")
            .First(line => line.StartsWith(name + ":", StringComparison.Ordinal))
            .Split('\t', StringSplitOptions.RemoveEmptyEntries)[1];
        AssertFollows(Docker("run"), flag, $"{Field("Uid")}:{Field("Gid")}");
    }

    [Then("the arguments of the docker run hold {string} and then {string}, and {string} and then {string}")]
    public void ThenRunHoldsTwo(string flag1, string value1, string flag2, string value2)
    {
        AssertFollows(Docker("run"), flag1, value1);
        AssertFollows(Docker("run"), flag2, value2);
    }

    [Then("the arguments of the docker run hold {string} and then {string}")]
    public void ThenRunHolds(string flag, string value) => AssertFollows(Docker("run"), flag, value);

    [Then("the arguments of the docker run hold {string} and then the path of that work tree followed by {string}")]
    public void ThenRunHoldsWorkTreePath(string flag, string suffix) => AssertFollows(Docker("run"), flag, workTree.Path + suffix);

    [Then("the arguments of the docker run hold {string} and then the path of a directory under {string} of the root directory followed by {string}")]
    public void ThenRunHoldsRootPath(string flag, string parent, string suffix)
    {
        Assert.Equal(HomeSuffix, suffix);
        string home = MountedHome(Docker("run"));
        AssertFollows(Docker("run"), flag, home + suffix);
        Assert.StartsWith(Path.Combine(_directory.FullName, parent), home, StringComparison.Ordinal);
        Assert.NotEqual(Path.Combine(_directory.FullName, parent), home + "/");
    }

    [Then("no argument of the docker run is {string}")]
    public void ThenRunLacks(string argument) => Assert.DoesNotContain(argument, Docker("run").Arguments);

    [Then("the home directory of that sandbox existed when the docker run started")]
    public void ThenHomeExistedAtRun() => Assert.Contains(MountedHome(Docker("run")), _homesAtRun);

    [Then("the two docker runs mount two different home directories")]
    public void ThenTwoHomes()
    {
        List<string> homes = [.. _recorded.Where(spec => spec.Arguments[0] is "run").Select(MountedHome)];
        Assert.Equal(2, homes.Count);
        Assert.NotEqual(homes[0], homes[1]);
    }

    [Then("the file {string} is under {string} of the root directory")]
    public void ThenFileUnderRoot(string relativePath, string parent)
    {
        string home = MountedHome(Docker("run"));
        Assert.StartsWith(Path.Combine(_directory.FullName, parent), home, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(home, relativePath)));
    }

    [Then("the directory {string} of that work tree existed when the docker run started")]
    public void ThenExistedAtRun(string relativePath) => Assert.Contains(relativePath, _directoriesAtRun);

    [Then("the start throws a SandboxException whose message holds the standard error of docker run")]
    public void ThenThrows()
    {
        Assert.IsType<SandboxException>(exceptionState.Exception);
        Assert.Contains(_run.StandardError, exceptionState.Exception.Message, StringComparison.Ordinal);
    }

    [Then("the runner ran a CommandSpec whose program is {string} and whose first argument is {string}")]
    public void ThenFirstArgument(string program, string first)
    {
        CommandSpec spec = _recorded[^1];
        Assert.Equal(program, spec.Command);
        Assert.Equal(first, spec.Arguments[0]);
    }

    [Then("the last three arguments are the container id with no line feed, {string} and {string}, in that order")]
    public void ThenLastThree(string program, string argument) =>
        Assert.Equal([ContainerId, program, argument], _recorded[^1].Arguments.TakeLast(3));

    [Then("the arguments of the docker exec hold {string} and then {string}, and {string} and then {string}")]
    public void ThenExecHolds(string flag1, string value1, string flag2, string value2)
    {
        AssertFollows(Docker("exec"), flag1, value1);
        AssertFollows(Docker("exec"), flag2, value2);
    }

    [Then("each of those is before the container id")]
    public void ThenBeforeId()
    {
        IReadOnlyList<string> arguments = Docker("exec").Arguments;
        int id = arguments.ToList().IndexOf(ContainerId);
        Assert.True(id > arguments.ToList().LastIndexOf("SECOND_SECRET"));
        Assert.True(id > arguments.ToList().LastIndexOf("FIRST_SECRET"));
    }

    [Then("no argument of the second docker exec is {string}")]
    public void ThenNotHeld(string name)
    {
        List<CommandSpec> execs = [.. _recorded.Where(s => s.Arguments[0] == "exec")];
        Assert.Equal(2, execs.Count);
        Assert.DoesNotContain(name, execs[1].Arguments);
    }

    [Then("the result has the exit code {int}, the standard output {string} and the standard error {string}")]
    public void ThenResult(int code, string output, string error)
    {
        Assert.Equal(code, _result!.ExitCode);
        Assert.Equal(output, _result.StandardOutput);
        Assert.Equal(error, _result.StandardError);
    }

    [Then("the runner ran a CommandSpec whose program is {string} and whose arguments are {string}, {string} and the container id, in that order")]
    public void ThenRemoved(string program, string first, string second)
    {
        CommandSpec spec = _recorded[^1];
        Assert.Equal(program, spec.Command);
        Assert.Equal([first, second, ContainerId], spec.Arguments);
    }
}
