using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class HostShellSteps(ExceptionState exceptionState) : IDisposable
{
    private static readonly string[] Allowlist = ["PATH", "HOME", "TMPDIR", "LANG"];
    private readonly DirectoryInfo _workTree = Directory.CreateTempSubdirectory("factory-hostshell-tree-");
    private readonly List<CommandSpec> _recorded = [];
    private readonly List<string> _setNames = [];
    private readonly List<string> _passedNames = [];
    private HostShell? _shell;
    private Mock<CommandRunner>? _mock;

    public void Dispose()
    {
        foreach (string name in _setNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        _workTree.Delete(recursive: true);
    }

    [Given("a HostShell over a new work tree and a mock CommandRunner that records every CommandSpec")]
    public void GivenAHostShell()
    {
        _mock = new Mock<CommandRunner>(MockBehavior.Strict);
        _mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(new CommandResult());
            });
        _shell = new HostShell(_mock.Object, _workTree.FullName);
    }

    [Given("the process environment holds these variables:")]
    public void GivenTheProcessEnvironmentHolds(Table table)
    {
        foreach (DataTableRow row in table.Rows)
        {
            _setNames.Add(row["name"]);
            Environment.SetEnvironmentVariable(row["name"], row["value"]);
        }
    }

    [Given("the process environment holds each of PATH, HOME, TMPDIR and LANG")]
    public void GivenTheProcessEnvironmentHoldsTheAllowlist()
    {
        // A name that the process lacks gets a value that changes nothing: TMPDIR names the temp directory that the process already uses.
        Dictionary<string, string> fallbacks = new(StringComparer.Ordinal)
        {
            ["PATH"] = "/usr/bin:/bin",
            ["HOME"] = Path.GetTempPath(),
            ["TMPDIR"] = Path.GetTempPath(),
            ["LANG"] = "C.UTF-8",
        };
        foreach (string name in Allowlist.Where(name => Environment.GetEnvironmentVariable(name) is null))
        {
            _setNames.Add(name);
            Environment.SetEnvironmentVariable(name, fallbacks[name]);
        }
    }

    [Given("the work tree of the host shell holds a directory {string}")]
    public void GivenTheWorkTreeHoldsADirectory(string path) =>
        Directory.CreateDirectory(Path.Combine(_workTree.FullName, path));

    [When("the host shell runs {string} with the arguments {string}")]
    public Task WhenRuns(string program, string arguments) => RunAsync(program, arguments, [], null);

    [When("the host shell runs {string} with the arguments {string} in its scratch path {string}")]
    public Task WhenRunsIn(string program, string arguments, string name) =>
        RunAsync(program, arguments, [], _shell!.ScratchPath(name));

    [When("the host shell runs {string} with the arguments {string} and the names {string}")]
    public Task WhenRunsWithNames(string program, string arguments, string names)
    {
        _passedNames.AddRange(names.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return RunAsync(program, arguments, _passedNames, null);
    }

    [When("the test gate runs over the host shell with the integration suite skipped")]
    public async Task WhenTheTestGateRuns()
    {
        Directory.CreateDirectory(Path.Combine(_workTree.FullName, "specs", "features"));
        var gate = new TestGate(_shell!, skipIntegration: true);
        await gate.RunAsync(_workTree.FullName, Pin.Take(_workTree.FullName), [], TestContext.Current.CancellationToken);
    }

    [When("the lint gate runs over the host shell with restore first")]
    public async Task WhenTheLintGateRuns()
    {
        var gate = new LintGate(_shell!, new Git(_mock!.Object), restoreFirst: true);
        await gate.RunAsync(_workTree.FullName, "Format", "Ada Lovelace", "ada@example.com", TestContext.Current.CancellationToken);
    }

    [Then("the host shell's CommandSpec has the program {string} and the arguments {string}")]
    public void ThenHasTheProgramAndArguments(string program, string arguments)
    {
        CommandSpec spec = Assert.Single(_recorded);
        Assert.Equal(program, spec.Command);
        Assert.Equal(arguments.Split(' '), spec.Arguments);
    }

    [Then("the host shell's CommandSpec runs in the work tree")]
    public void ThenRunsInTheWorkTree() => Assert.Equal(_workTree.FullName, Assert.Single(_recorded).WorkingDirectory);

    [Then("the host shell's CommandSpec runs in its scratch path {string}")]
    public void ThenRunsInTheScratchPath(string name) =>
        Assert.Equal(_shell!.ScratchPath(name), Assert.Single(_recorded).WorkingDirectory);

    [Then("the host shell's CommandSpec clears the environment")]
    public void ThenClearsTheEnvironment() => Assert.True(Assert.Single(_recorded).ClearEnvironment);

    [Then("the host shell's CommandSpec holds these environment variables:")]
    public void ThenHoldsTheseVariables(Table table)
    {
        CommandSpec spec = Assert.Single(_recorded);
        foreach (DataTableRow row in table.Rows)
        {
            Assert.True(spec.EnvironmentVariables.TryGetValue(row["name"], out string? value), $"no variable {row["name"]}");
            Assert.Equal(row["value"], value);
        }
    }

    [Then("the host shell's CommandSpec holds each of PATH, HOME, TMPDIR and LANG with its value in the process environment")]
    public void ThenHoldsTheAllowlist()
    {
        CommandSpec spec = Assert.Single(_recorded);
        foreach (string name in Allowlist)
        {
            Assert.True(spec.EnvironmentVariables.TryGetValue(name, out string? value), $"no variable {name}");
            Assert.Equal(Environment.GetEnvironmentVariable(name), value);
        }
    }

    [Then("every other variable of the host shell's CommandSpec has a name that starts {string} or {string}")]
    public void ThenEveryOtherVariableStarts(string first, string second)
    {
        CommandSpec spec = Assert.Single(_recorded);
        Assert.All(
            spec.EnvironmentVariables.Keys.Where(name => !Allowlist.Contains(name) && !_passedNames.Contains(name)),
            name => Assert.True(
                name.StartsWith(first, StringComparison.Ordinal) || name.StartsWith(second, StringComparison.Ordinal),
                $"the variable {name} is outside the allowlist"));
    }

    [Then("the host shell's CommandSpec lacks the environment variable {string}")]
    public void ThenLacks(string name) => Assert.DoesNotContain(name, Assert.Single(_recorded).EnvironmentVariables.Keys);

    [Then("the host shell throws a GitCommandException whose message holds {string}")]
    public void ThenThrowsGitCommandException(string text)
    {
        Assert.IsType<GitCommandException>(exceptionState.Exception);
        Assert.Contains(text, exceptionState.Exception.Message, StringComparison.Ordinal);
    }

    [Then("the mock CommandRunner of the host shell recorded no CommandSpec")]
    public void ThenRecordedNone() => Assert.Empty(_recorded);

    [Then("the host shell's scratch path for {string} is {string} under the temp directory of the process")]
    public void ThenTheScratchPathIs(string name, string child) =>
        Assert.Equal(Path.Combine(Path.GetTempPath(), child), _shell!.ScratchPath(name));

    [Then("the results directory of the test command is a directory of the work tree whose name starts {string}")]
    public void ThenTheResultsDirectoryIsUnderTheWorkTree(string prefix)
    {
        CommandSpec test = Assert.Single(_recorded, spec => spec.Command == "dotnet" && spec.Arguments[0] == "test");
        int index = test.Arguments.ToList().IndexOf("--results-directory");
        Assert.True(index >= 0, "the test command has no --results-directory");
        string directory = test.Arguments[index + 1];
        Assert.Equal(_workTree.FullName, Path.GetDirectoryName(directory));
        Assert.StartsWith(prefix, Path.GetFileName(directory), StringComparison.Ordinal);
        Assert.Equal(_workTree.FullName, test.WorkingDirectory);
    }

    [Then("the host shell ran {string} and then {string}")]
    public void ThenRanInOrder(string first, string second)
    {
        List<string> dotnet = [.. _recorded.Where(spec => spec.Command == "dotnet").Select(spec => $"dotnet {string.Join(' ', spec.Arguments)}")];
        Assert.Equal([first, second], dotnet);
    }

    private async Task RunAsync(string program, string arguments, IReadOnlyList<string> names, string? workingDirectory)
    {
        exceptionState.Exception = null;
        try
        {
            await _shell!.RunAsync(program, arguments.Split(' '), names, workingDirectory, TestContext.Current.CancellationToken);
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }
}
