using Core;
using Moq;
using Reqnroll;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class GitCheckoutBranchSteps
{
    private int _exitCode;
    private Exception? _exception;

    [Given("git exits with the code {int} on the checkout of a branch")]
    public void GivenExit(int code) => _exitCode = code;

    [When("a caller checks out the branch {string} in the directory of a clone")]
    public async Task WhenCheckout(string branch)
    {
        var mock = new Mock<CommandRunner>(MockBehavior.Strict);
        mock.Setup(r => r.RunAsync(It.IsAny<CommandSpec>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new CommandResult { ExitCode = _exitCode, StandardError = "error: pathspec did not match" }));
        try
        {
            await new Git(mock.Object).CheckoutExistingBranchAsync(branch, "/tmp/clone", TestContext.Current.CancellationToken);
        }
        catch (GitCommandException ex)
        {
            _exception = ex;
        }
    }

    [Then("Git throws GitCommandException")]
    public void ThenThrows() => Assert.IsType<GitCommandException>(_exception);

    [Then("Git throws no exception")]
    public void ThenNone() => Assert.Null(_exception);
}
