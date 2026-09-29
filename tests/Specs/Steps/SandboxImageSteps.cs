using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class SandboxImageSteps(ExceptionState exceptionState) : IDisposable
{
    private readonly List<CommandSpec> _recorded = [];
    private readonly Dictionary<string, CommandResult> _results = new();
    private string _directory = "";

    [Given("a checkout directory whose global.json holds the sdk.version {string}")]
    public void GivenACheckoutDirectory(string version)
    {
        _directory = Directory.CreateTempSubdirectory("sandbox-image-").FullName;
        File.WriteAllText(Path.Combine(_directory, "global.json"), $"{{\"sdk\":{{\"version\":\"{version}\"}}}}");
    }

    [Given("docker image inspect exits with the code {int}")]
    public void GivenInspectExits(int code) => _results["image"] = new CommandResult { ExitCode = code };

    [Given("docker build exits with the code {int}")]
    public void GivenBuildExits(int code) => _results["build"] = new CommandResult { ExitCode = code };

    [Given("docker build exits with a code other than 0 and writes a reason to standard error")]
    public void GivenBuildFails() => _results["build"] = new CommandResult { ExitCode = 1, StandardError = "no space left on device" };

    [When("SandboxImage ensures the image {string} for the checkout directory")]
    public async Task WhenEnsures(string tag)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(m => m.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns((CommandSpec spec, CancellationToken _) =>
            {
                _recorded.Add(spec);
                return Task.FromResult(_results[spec.Arguments[0]]);
            });
        try
        {
            await new SandboxImage(mock.Object).EnsureAsync(tag, _directory, TestContext.Current.CancellationToken);
        }
        catch (SandboxImageException ex)
        {
            exceptionState.Exception = ex;
        }
    }

    [Then("the first command runs the program {string} with the arguments {string}, {string} and {string} in that order")]
    public void ThenFirstCommand(string program, string a, string b, string c)
    {
        Assert.Equal(program, _recorded[0].Command);
        Assert.Equal([a, b, c], _recorded[0].Arguments);
    }

    [Then("the runner has run {int} command")]
    public void ThenRunnerHasRun(int count) => Assert.Equal(count, _recorded.Count);

    [Then("the operation completes")]
    public void ThenCompletes() => Assert.Null(exceptionState.Exception);

    [Then("the second command runs the program {string} with the first argument {string} and the arguments {string} then {string}")]
    public void ThenSecondCommand(string program, string first, string flag, string value)
    {
        CommandSpec spec = _recorded[1];
        Assert.Equal(program, spec.Command);
        Assert.Equal(first, spec.Arguments[0]);
        AssertPair(spec, flag, value);
    }

    [Then("the build arguments hold {string} and then the path of sandbox\\/Dockerfile under the checkout directory")]
    public void ThenBuildFile(string flag)
        => AssertPair(_recorded[1], flag, Path.Combine(_directory, "sandbox", "Dockerfile"));

    [Then("the build arguments hold {string} and then {string}")]
    public void ThenBuildPair(string flag, string value) => AssertPair(_recorded[1], flag, value);

    [Then("the operation throws a SandboxImageException")]
    public void ThenThrows() => Assert.IsType<SandboxImageException>(exceptionState.Exception);

    [Then("the exception message holds the standard error of the build")]
    public void ThenMessageHolds()
        => Assert.Contains(_results["build"].StandardError, exceptionState.Exception!.Message, StringComparison.Ordinal);

    public void Dispose()
    {
        if (_directory.Length > 0)
        {
            Directory.Delete(_directory, true);
        }
    }

    private static void AssertPair(CommandSpec spec, string flag, string value)
    {
        int index = spec.Arguments.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"{flag} is missing");
        Assert.Equal(value, spec.Arguments[index + 1]);
    }
}
