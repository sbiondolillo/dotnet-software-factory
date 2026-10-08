using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class SandboxImageSteps(ExceptionState exceptionState, ScenarioContext scenarioContext) : IDisposable
{
    private readonly List<CommandSpec> _recorded = [];
    private readonly Dictionary<string, CommandResult> _results = [];
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("factory-sandbox-image-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Given("a checkout directory whose global.json holds the sdk.version {string}")]
    public void GivenACheckoutDirectory(string version) =>
        File.WriteAllText(Path.Combine(_directory.FullName, "global.json"), $"{{\"sdk\":{{\"version\":\"{version}\"}}}}");

    [Given("docker image inspect exits with the code {int}")]
    public void GivenInspectExits(int code)
    {
        _results["image"] = new CommandResult { ExitCode = code };
        scenarioContext["inspectExit"] = code;
    }

    [Given("docker build exits with the code {int}")]
    public void GivenBuildExits(int code) => _results["build"] = new CommandResult { ExitCode = code };

    [Given("docker build exits with a code other than 0 and writes a reason to standard error")]
    public void GivenBuildFails() =>
        _results["build"] = new CommandResult { ExitCode = 2, StandardError = "error: no space left on device" };

    [When("a caller asks for the image with the tag {string}")]
    public async Task WhenACallerAsks(string tag)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(runner => runner.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(_results[spec.Arguments[0]]);
            });
        exceptionState.Exception = null;
        try
        {
            await new SandboxImage(mock.Object).EnsureAsync(tag, _directory.FullName, TestContext.Current.CancellationToken);
        }
        catch (Exception ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [Then("the first command run is docker with the arguments {string}, {string} and {string} in that order")]
    public void ThenFirstCommand(string first, string second, string third)
    {
        Assert.Equal("docker", _recorded[0].Command);
        Assert.Equal([first, second, third], _recorded[0].Arguments);
    }

    [Then("SandboxImage runs no other command")]
    public void ThenNoOtherCommand() => Assert.Single(_recorded);

    [Then("the operation completes")]
    public void ThenCompletes() => Assert.Null(exceptionState.Exception);

    [Then("the second command run is docker with the first argument {string} and arguments holding {string} and then {string}")]
    public void ThenSecondCommand(string first, string flag, string value)
    {
        CommandSpec build = _recorded[1];
        Assert.Equal("docker", build.Command);
        Assert.Equal(first, build.Arguments[0]);
        AssertFollows(build, flag, value);
    }

    [Then("the build arguments hold {string} and then the path of sandbox\\/Dockerfile under the given directory")]
    public void ThenFilePath(string flag) =>
        AssertFollows(_recorded[1], flag, Path.Combine(_directory.FullName, "sandbox", "Dockerfile"));

    [Then("the build arguments hold {string} and then {string}")]
    public void ThenBuildArgs(string flag, string value) => AssertFollows(_recorded[1], flag, value);

    [Then("the operation throws a SandboxImageException")]
    public void ThenThrows() => Assert.IsType<SandboxImageException>(exceptionState.Exception);

    [Then("the exception message holds the standard error of the build")]
    public void ThenMessage() =>
        Assert.Contains(_results["build"].StandardError, exceptionState.Exception?.Message, StringComparison.Ordinal);

    private static void AssertFollows(CommandSpec spec, string flag, string value)
    {
        int index = spec.Arguments.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"no argument {flag}");
        Assert.Equal(value, spec.Arguments[index + 1]);
    }
}
