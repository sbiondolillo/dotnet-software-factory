using Core;
using Xunit;

namespace Specs.Unit;

public sealed class ApiGateTests
{
    private const string Greet = "Core: Core.Greeter::method public static System.String Greet(System.String)";
    private const string Greeter = "Core: class static Core.Greeter";
    private const string Extra = "Core: class static Core.Extra";

    [Fact]
    public void TwoEqualListingsGiveNoText() => Assert.Empty(ApiGate.Compare([Greet, Greeter], [Greeter, Greet]));

    [Fact]
    public void ALineOfTheBaseThatTheBuildLacksIsALostSymbol() =>
        Assert.Equal([$"lost symbol: {Greet}"], ApiGate.Compare([Greet, Greeter], [Greeter]));

    [Fact]
    public void ALineOfTheBuildThatTheBaseLacksIsANewSymbol() =>
        Assert.Equal([$"new symbol: {Extra}"], ApiGate.Compare([Greeter], [Greeter, Extra]));

    [Fact]
    public void LostSymbolsComeBeforeNewOnesAndEachSetIsOrdered() =>
        Assert.Equal(
            ["lost symbol: a", "lost symbol: b", "new symbol: c", "new symbol: d"],
            ApiGate.Compare(["b", "a", "k"], ["k", "d", "c"]));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Func<IReadOnlyList<string>, CommandResult> Printing(string lines) =>
        words => words is ["dotnet", ApiGate.Printer, ..] ? new CommandResult { StandardOutput = lines } : new CommandResult();

    [Fact]
    public async Task TheGateIsNamedApi()
    {
        await using var box = new GateSandbox();
        var gate = new ApiGate(await box.StartAsync(Ct), [Greeter]);
        Assert.Equal(GateName.Api, GateSandbox.NameOf(gate));
    }

    [Fact]
    public async Task AListingLikeTheBaseIsSameWithNoRedoText()
    {
        await using var box = new GateSandbox { Answer = Printing($"{Greeter}\n{Greet}\n") };
        var gate = new ApiGate(await box.StartAsync(Ct), [Greet, Greeter]);
        GateOutcome outcome = await GateSandbox.RunAsync(gate, Ct);
        Assert.Equal("same", outcome.Result);
        Assert.Empty(outcome.RedoTexts);
    }

    [Fact]
    public async Task ANewLineIsChangedWithItsRedoText()
    {
        await using var box = new GateSandbox { Answer = Printing($"{Greeter}\n{Extra}\n") };
        var gate = new ApiGate(await box.StartAsync(Ct), [Greeter]);
        GateOutcome outcome = await GateSandbox.RunAsync(gate, Ct);
        Assert.Equal("changed", outcome.Result);
        Assert.Equal([$"new symbol: {Extra}"], outcome.RedoTexts);
    }

    [Fact]
    public async Task AFailedListingIsChangedWithItsFault()
    {
        await using var box = new GateSandbox
        {
            Answer = words => words is ["dotnet", ApiGate.Printer, ..] ? new CommandResult { ExitCode = 3, StandardError = "no printer" } : new CommandResult(),
        };
        var gate = new ApiGate(await box.StartAsync(Ct), [Greeter]);
        GateOutcome outcome = await GateSandbox.RunAsync(gate, Ct);
        Assert.Equal("changed", outcome.Result);
        string text = Assert.Single(outcome.RedoTexts);
        Assert.Contains("The API printer exited with the code 3", text, StringComparison.Ordinal);
        Assert.Contains("no printer", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFactoryGivesAGateOverTheListingOfTheBase()
    {
        await using var box = new GateSandbox { Answer = Printing($"{Greeter}\n") };
        ApiGateStart start = await ApiGate.CreateAsync(await box.StartAsync(Ct), cancellationToken: Ct);
        Assert.Null(start.Stop);
        Assert.NotNull(start.Gate);
        box.Answer = Printing($"{Greeter}\n{Extra}\n");
        Assert.Equal([$"new symbol: {Extra}"], (await GateSandbox.RunAsync(start.Gate, Ct)).RedoTexts);
    }

    [Fact]
    public async Task TheFactoryOfAFailedListingGivesTheStopTextAndNoGate()
    {
        await using var box = new GateSandbox
        {
            Answer = words => words is ["dotnet", "build", ..] ? new CommandResult { ExitCode = 1, StandardOutput = "error CS0103" } : new CommandResult(),
        };
        ApiGateStart start = await ApiGate.CreateAsync(await box.StartAsync(Ct), cancellationToken: Ct);
        Assert.Null(start.Gate);
        Assert.NotNull(start.Stop);
        Assert.StartsWith("The public API of the base commit could not be listed, so there is no build:\n\ndotnet build exited with the code 1", start.Stop, StringComparison.Ordinal);
        Assert.Contains("error CS0103", start.Stop, StringComparison.Ordinal);
    }
}
