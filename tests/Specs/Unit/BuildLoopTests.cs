using Core;
using Xunit;

namespace Specs.Unit;

/// <summary>The build loop as a state machine: each state, each input and the next state, read from the calls, the rows, the
/// progress lines and the result.</summary>
public sealed class BuildLoopTests
{
    private static readonly GateName[] AllGates = [GateName.Scope, GateName.Test, GateName.Api, GateName.Check];

    [Fact]
    public async Task AnErrorOfTheBuildStopsWithTheBuildRowFailedAndNoLint()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.AddRange(AllGates);
        h.Builds[0] = BuildLoopHarness.BuildStop("The API answered 529.");
        await h.RunAsync();

        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Stopped, 0, "The API answered 529."), h.Result);
        Assert.Equal(["build"], h.Calls);
        Assert.Equal(["| build 1 | 5 | 0 | $0 | failed |"], h.Rows);
        Assert.Equal(["build: start"], h.Lines);
    }

    [Fact]
    public async Task RedoTextsOfTheBuildLintThenRedoAfterTheBuildRowWithNoGate()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.Add(GateName.Test);
        h.Builds.Clear();
        h.Builds.AddRange([BuildLoopHarness.BuildRedo("The report is absent."), BuildLoopHarness.Pass]);
        await h.RunAsync();

        Assert.Equal(["build", "lint", "build", "lint", "test", "review"], h.Calls);
        Assert.Equal(["- Redo 1, after build 1: The report is absent."], h.RedoItems);
        Assert.Equal([[], ["The report is absent."]], h.BuildTexts);
        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Passed, 1), h.Result);
    }

    [Fact]
    public async Task APassingBuildRunsTheLintEveryGateInOrderAndTheReview()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.AddRange(AllGates);
        await h.RunAsync();

        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Passed, 0), h.Result);
        Assert.Equal(["build", "lint", "scope", "test", "api", "check", "review"], h.Calls);
        Assert.Equal(
            [
                "build: start", "lint: start",
                "scope: start", "scope: passed", "test: start", "test: passed",
                "api: start", "api: passed", "check: start", "check: passed",
                "review: start", "review: approved",
            ],
            h.Lines);
        Assert.Equal(
            [
                "| build 1 | 5 | 0 | $0 | passed |",
                "| scope 1 | 2 | 0 | $0 | passed |",
                "| test 1 | 2 | 0 | $0 | passed |",
                "| api 1 | 2 | 0 | $0 | passed |",
                "| check 1 | 2 | 0 | $0 | passed |",
                "| review 1 | 7 | 3 | $0.5 | approved |",
            ],
            h.Rows);
        Assert.Empty(h.RedoItems);
    }

    [Fact]
    public async Task ALoopWithNoGateRunsTheReviewAfterTheLint()
    {
        await using var h = new BuildLoopHarness();
        await h.RunAsync();

        Assert.Equal(["build", "lint", "review"], h.Calls);
        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Passed, 0), h.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TheFirstGateThatFailsEndsTheGatesAndTheRedoFollowsItsRow(int failing)
    {
        await using var h = new BuildLoopHarness();
        h.Gates.AddRange(AllGates);
        GateName name = AllGates[failing];
        h.GateAnswers[name] = [BuildLoopHarness.GateFails("fault one\nmore detail", "fault two"), () => new GateOutcome("passed", [])];
        await h.RunAsync();

        string[] first = ["build", "lint", .. AllGates.Take(failing + 1).Select(gate => gate.Text())];
        string[] second = ["build", "lint", .. AllGates.Select(gate => gate.Text()), "review"];
        Assert.Equal([.. first, .. second], h.Calls);
        Assert.Equal([$"- Redo 1, after {name.Text()} 1: fault one", $"- Redo 1, after {name.Text()} 1: fault two"], h.RedoItems);
        Assert.Equal([[], ["fault one\nmore detail", "fault two"]], h.BuildTexts);
        Assert.Contains($"| {name.Text()} 1 | 2 | 0 | $0 | failed |", h.Rows);
        Assert.Contains($"{name.Text()}: failed", h.Lines);
        Assert.Contains("redo: 1", h.Lines);
        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Passed, 1), h.Result);
    }

    [Fact]
    public async Task ALostTestUnderPassedStillGivesARedo()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.AddRange([GateName.Test, GateName.Check]);
        h.GateAnswers[GateName.Test] = [() => new GateOutcome("passed", ["lost test: GreeterTests.Hello"]), () => new GateOutcome("passed", [])];
        await h.RunAsync();

        Assert.Equal(["build", "lint", "test", "build", "lint", "test", "check", "review"], h.Calls);
        Assert.Equal(["- Redo 1, after test 1: lost test: GreeterTests.Hello"], h.RedoItems);
    }

    [Fact]
    public async Task AnErrorOfTheReviewStopsWithTheReviewRowFailed()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.Add(GateName.Test);
        h.Reviews[0] = BuildLoopHarness.ReviewError("The API answered 500.");
        await h.RunAsync();

        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Stopped, 0, "The API answered 500."), h.Result);
        Assert.Equal("| review 1 | 7 | 3 | $0.5 | failed |", h.Rows[^1]);
        Assert.Equal("review: start", h.Lines[^1]);
        Assert.Equal(["build", "lint", "test", "review"], h.Calls);
    }

    [Fact]
    public async Task ABlockedReviewGetsARedoAfterTheReviewRow()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.Add(GateName.Test);
        h.Reviews.Clear();
        h.Reviews.AddRange([BuildLoopHarness.ReviewBlocks("Bind the scenario Hello."), BuildLoopHarness.Approve]);
        await h.RunAsync();

        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Passed, 1), h.Result);
        Assert.Equal(["- Redo 1, after review 1: Bind the scenario Hello."], h.RedoItems);
        Assert.Equal([[], ["Bind the scenario Hello."]], h.BuildTexts);
        Assert.Contains("| review 1 | 7 | 3 | $0.5 | blocked |", h.Rows);
        Assert.Equal(["review: blocked", "redo: 1", "build: start"], h.Lines.SkipWhile(line => line is not "review: blocked").Take(3));
    }

    [Fact]
    public async Task TheFourthRejectionSpendsTheBudgetWithTheLastTexts()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.Add(GateName.Test);
        h.Reviews.Clear();
        h.Reviews.AddRange([
            BuildLoopHarness.ReviewBlocks("first"),
            BuildLoopHarness.ReviewBlocks("second"),
            BuildLoopHarness.ReviewBlocks("third"),
            BuildLoopHarness.ReviewBlocks("last one", "last two"),
        ]);
        await h.RunAsync();

        Assert.Equal(BuildLoop.RedoBudget, 3);
        Assert.Equal(
            new BuildLoopResult(BuildLoopEnd.Spent, 3, "The budget of 3 Redos is spent. The last build still fails:\n\nlast one\n\nlast two"),
            h.Result);
        Assert.Equal(4, h.Calls.Count(call => call is "build"));
        Assert.Equal(["redo: 1", "redo: 2", "redo: 3"], h.Lines.Where(line => line.StartsWith("redo: ", StringComparison.Ordinal)));
        Assert.Equal(
            ["- Redo 1, after review 1: first", "- Redo 2, after review 2: second", "- Redo 3, after review 3: third"],
            h.RedoItems);
        Assert.Equal("review: blocked", h.Lines[^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AStopAfterRedosKeepsTheRedosSpent(int redos)
    {
        await using var h = new BuildLoopHarness();
        h.Gates.Add(GateName.Test);
        h.Builds.Clear();
        h.Builds.AddRange([.. Enumerable.Repeat<Func<BuildOutcome>>(BuildLoopHarness.Pass, redos), BuildLoopHarness.BuildStop("down")]);
        h.Reviews[0] = BuildLoopHarness.ReviewBlocks("again");
        await h.RunAsync();

        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Stopped, redos, "down"), h.Result);
        Assert.Equal(redos + 1, h.Calls.Count(call => call is "build"));
        Assert.Equal(redos, h.RedoItems.Count);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(true, " \n")]
    [InlineData(false, "")]
    [InlineData(false, "\t")]
    public async Task AnErrorWithNoTextStopsWithTheFixedSentence(bool ofBuild, string text)
    {
        await using var h = new BuildLoopHarness();
        if (ofBuild)
        {
            h.Builds[0] = BuildLoopHarness.BuildStop(text);
        }
        else
        {
            h.Reviews[0] = BuildLoopHarness.ReviewError(text);
        }

        await h.RunAsync();

        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Stopped, 0, "claude answered with an error and no text."), h.Result);
        Assert.Equal(BuildLoop.NoTextError, h.Result!.Comment);
    }

    [Fact]
    public async Task AReviewErrorWithNoBlockingTextStopsWithTheFixedSentence()
    {
        await using var h = new BuildLoopHarness();
        h.Reviews[0] = () => new ReviewPhaseResult(false, [], IsError: true);
        await h.RunAsync();

        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Stopped, 0, BuildLoop.NoTextError), h.Result);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("lint")]
    [InlineData("scope")]
    [InlineData("test")]
    [InlineData("api")]
    [InlineData("check")]
    [InlineData("review")]
    public async Task ACancelThrowsToTheCallerAndNoLaterCallRuns(string point)
    {
        await using var h = new BuildLoopHarness();
        h.Gates.AddRange(AllGates);
        switch (point)
        {
            case "build":
                h.Builds[0] = h.Cancels<BuildOutcome>(BuildLoopHarness.Pass);
                break;
            case "lint":
                h.Formats[0] = h.Cancels(() => new CommandResult());
                break;
            case "review":
                h.Reviews[0] = h.Cancels<ReviewPhaseResult>(BuildLoopHarness.Approve);
                break;
            default:
                GateName gate = AllGates.Single(name => name.Text() == point);
                h.GateAnswers[gate] = [h.Cancels(() => new GateOutcome("passed", []))];
                break;
        }

        await h.RunAsync();

        Assert.IsAssignableFrom<OperationCanceledException>(h.Thrown);
        Assert.Null(h.Result);
        Assert.Equal(point, h.Calls[^1]);
        Assert.Single(h.Calls, call => call == point);
    }

    [Fact]
    public async Task ATokenCancelledBeforeTheLoopRunsNoCall()
    {
        await using var h = new BuildLoopHarness();
        await h.Source.CancelAsync();
        await h.RunAsync();

        Assert.IsAssignableFrom<OperationCanceledException>(h.Thrown);
        Assert.Empty(h.Calls);
        Assert.Empty(h.Lines);
    }

    [Theory]
    [InlineData("build", new string[0])]
    [InlineData("lint", new string[0])]
    [InlineData("scope", new[] { "| build 1 | 5 | 0 | $0 | passed |" })]
    [InlineData("test", new[] { "| build 1 | 5 | 0 | $0 | passed |", "| scope 1 | 2 | 0 | $0 | passed |" })]
    [InlineData("api", new[] { "| build 1 | 5 | 0 | $0 | passed |", "| scope 1 | 2 | 0 | $0 | passed |", "| test 1 | 2 | 0 | $0 | passed |" })]
    [InlineData("check", new[] { "| build 1 | 5 | 0 | $0 | passed |", "| scope 1 | 2 | 0 | $0 | passed |", "| test 1 | 2 | 0 | $0 | passed |", "| api 1 | 2 | 0 | $0 | passed |" })]
    [InlineData("review", new[] { "| build 1 | 5 | 0 | $0 | passed |", "| scope 1 | 2 | 0 | $0 | passed |", "| test 1 | 2 | 0 | $0 | passed |", "| api 1 | 2 | 0 | $0 | passed |", "| check 1 | 2 | 0 | $0 | passed |" })]
    public async Task AnExceptionOfADependencyReachesTheCallerUnchangedAndTheRowsSoFarStay(string point, string[] rows)
    {
        await using var h = new BuildLoopHarness();
        h.Gates.AddRange(AllGates);
        var thrown = new InvalidOperationException("boom");
        switch (point)
        {
            case "build":
                h.Builds[0] = () => throw thrown;
                break;
            case "lint":
                h.Formats[0] = () => new CommandResult { ExitCode = 1, StandardError = "format broke" };
                break;
            case "review":
                h.Reviews[0] = () => throw thrown;
                break;
            default:
                h.GateAnswers[AllGates.Single(name => name.Text() == point)] = [() => throw thrown];
                break;
        }

        await h.RunAsync();

        if (point is "lint")
        {
            Assert.IsType<LintGateException>(h.Thrown);
        }
        else
        {
            Assert.Same(thrown, h.Thrown);
        }

        Assert.Null(h.Result);
        Assert.Equal(point, h.Calls[^1]);
        Assert.Equal(rows, h.Rows);
    }

    [Fact]
    public async Task TheLintRunsAfterEveryBuildThatPasses()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.Add(GateName.Test);
        h.Builds.Clear();
        h.Builds.AddRange([BuildLoopHarness.Pass, BuildLoopHarness.Pass, BuildLoopHarness.BuildStop("down")]);
        h.Reviews[0] = BuildLoopHarness.ReviewBlocks("again");
        await h.RunAsync();

        Assert.Equal(
            ["build", "lint", "test", "review", "build", "lint", "test", "review", "build"],
            h.Calls);
    }

    [Fact]
    public async Task EachGateRowTakesTheClockTimeOfItsOwnRun()
    {
        await using var h = new BuildLoopHarness();
        h.Gates.AddRange([GateName.Test, GateName.Check]);
        h.GateAnswers[GateName.Test] = [() =>
        {
            h.Clock.Advance(40);
            return new GateOutcome("passed", []);
        }];
        await h.RunAsync();

        Assert.Equal(["| build 1 | 5 | 0 | $0 | passed |", "| test 1 | 42 | 0 | $0 | passed |", "| check 1 | 2 | 0 | $0 | passed |", "| review 1 | 7 | 3 | $0.5 | approved |"], h.Rows);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OnTheManagedPathTheLintCommitIsPushedBeforeTheGates(bool commits, bool managed)
    {
        await using var h = new BuildLoopHarness { Managed = managed, Status = commits ? " M src/Greeter.cs\n" : "" };
        h.Gates.Add(GateName.Test);
        await h.RunAsync();

        Assert.Equal(commits && managed ? ["build", "lint", "push", "test", "review"] : ["build", "lint", "test", "review"], h.Calls);
    }

    [Fact]
    public async Task ACancelDuringThePushOfTheLintCommitThrowsAndNoGateRuns()
    {
        await using var h = new BuildLoopHarness { Managed = true, Status = " M src/Greeter.cs\n" };
        h.Gates.Add(GateName.Test);
        h.Pushes[0] = h.Cancels(() => new CommandResult());
        await h.RunAsync();

        Assert.IsAssignableFrom<OperationCanceledException>(h.Thrown);
        Assert.Equal(["build", "lint", "push"], h.Calls);
        Assert.Empty(h.Rows);
    }

    [Fact]
    public async Task ACancelDuringTheLintThrowsBeforeThePush()
    {
        await using var h = new BuildLoopHarness { Managed = true, Status = " M src/Greeter.cs\n" };
        h.Gates.Add(GateName.Test);
        h.Formats[0] = h.Cancels(() => new CommandResult());
        await h.RunAsync();

        Assert.IsAssignableFrom<OperationCanceledException>(h.Thrown);
        Assert.Equal(["build", "lint"], h.Calls);
    }

    [Fact]
    public async Task AReviewErrorStopsWithItsFirstText()
    {
        await using var h = new BuildLoopHarness();
        h.Reviews[0] = () => new ReviewPhaseResult(false, ["first", "second"], IsError: true);
        await h.RunAsync();

        Assert.Equal(new BuildLoopResult(BuildLoopEnd.Stopped, 0, "first"), h.Result);
    }

    [Theory]
    [InlineData("build: start", new string[0])]
    [InlineData("test: start", new[] { "build", "lint" })]
    [InlineData("review: start", new[] { "build", "lint", "test" })]
    [InlineData("redo: 1", new[] { "build", "lint", "test", "review" })]
    public async Task ACancelDuringAProgressWriteThrowsBeforeTheNextCall(string line, string[] calls)
    {
        await using var h = new BuildLoopHarness();
        h.Gates.Add(GateName.Test);
        h.Reviews[0] = BuildLoopHarness.ReviewBlocks("again");
        h.CancelOnLine = line;
        await h.RunAsync();

        Assert.IsAssignableFrom<OperationCanceledException>(h.Thrown);
        Assert.Equal(calls, h.Calls);
        Assert.Equal(line, h.Lines[^1]);
    }
}
