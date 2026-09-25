using Core;
using Moq;
using Xunit;

namespace Specs.Unit;

public class CommandCallerTests
{
    [Fact]
    public async Task ACallerGivenAMockWhoseRunAsyncReturnsASucceedingResultPassesItThroughUnchangedAndStartsNoProgram()
    {
        CommandResult received = await RunAgainstMock(new CommandResult(0, "canned output", ""));

        Assert.Equal(new CommandResult(0, "canned output", ""), received);
    }

    [Fact]
    public async Task ACallerGivenAMockWhoseRunAsyncReturnsAFailingResultPassesItThroughUnchangedAndStartsNoProgram()
    {
        CommandResult received = await RunAgainstMock(new CommandResult(2, "", "canned error"));

        Assert.Equal(new CommandResult(2, "", "canned error"), received);
    }

    private static async Task<CommandResult> RunAgainstMock(CommandResult canned)
    {
        var mock = new Mock<CommandRunner>();
        mock
            .Setup(runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(canned);
        var caller = new CommandCaller(mock.Object);

        string[] arguments = { "--version" };
        CommandResult received = await caller.RunAsync("sh", arguments);

        mock.Verify(
            runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<TimeSpan>()),
            Times.Once());
        return received;
    }
}
