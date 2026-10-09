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
}
