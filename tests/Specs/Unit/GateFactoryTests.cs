using Core;
using Xunit;

namespace Specs.Unit;

public sealed class GateFactoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<RecipeChoice> Choices() =>
    [
        new RecipeChoice(Recipe.Behavior, [], null),
        new RecipeChoice(Recipe.Refactor, [], null),
        new RecipeChoice(Recipe.Refactor, [], "true"),
        new RecipeChoice(Recipe.Cleanup, ["README.md"], "true"),
        new RecipeChoice(Recipe.Reframe, ["src/"], "true"),
    ];

    [Theory]
    [MemberData(nameof(Choices))]
    public async Task EachGateIsNamedAfterItsSlotOfTheList(RecipeChoice choice)
    {
        await using var box = new GateSandbox
        {
            Answer = words => words is ["dotnet", ApiGate.Printer, ..] ? new CommandResult { StandardOutput = "Core: class static Core.Greeter\n" } : new CommandResult(),
        };
        Sandbox sandbox = await box.StartAsync(Ct);
        var factory = new GateFactory(sandbox, new Git(new CommandRunner()), box.WorkTree.FullName, "base", []);

        GateSet set = await factory.CreateAsync(choice, Pin.Take(box.WorkTree.FullName), Ct);

        Assert.Null(set.Stop);
        Assert.Equal(RecipeRules.Gates(choice), set.Gates.Select(GateSandbox.NameOf));
        Assert.All(set.Gates, gate => Assert.Equal(gate.Name switch
        {
            GateName.Scope => typeof(ScopeGate),
            GateName.Test => typeof(TestGate),
            GateName.Api => typeof(ApiGate),
            _ => typeof(CheckGate),
        }, gate.GetType()));
    }

    [Fact]
    public async Task OnlyAChoiceWithAScopeReadsTheBaselineOfTheTests()
    {
        await using var box = new GateSandbox();
        Sandbox sandbox = await box.StartAsync(Ct);
        var factory = new GateFactory(sandbox, new Git(new CommandRunner()), box.WorkTree.FullName, "base", []);

        await factory.CreateAsync(new RecipeChoice(Recipe.Refactor, [], "true"), Pin.Take(box.WorkTree.FullName), Ct);
        Assert.Empty(box.Seen);

        await factory.CreateAsync(new RecipeChoice(Recipe.Cleanup, ["README.md"], "true"), Pin.Take(box.WorkTree.FullName), Ct);
        Assert.Contains(box.Seen, words => words is ["dotnet", "test", ..]);
    }

    [Fact]
    public async Task AFailedListingOfTheBaseGivesTheStopTextAndNoGate()
    {
        await using var box = new GateSandbox
        {
            Answer = words => words is ["dotnet", ApiGate.Printer, ..] ? new CommandResult { ExitCode = 3, StandardError = "no printer" } : new CommandResult(),
        };
        Sandbox sandbox = await box.StartAsync(Ct);
        using var progress = new StringWriter();
        var factory = new GateFactory(sandbox, new Git(new CommandRunner()), box.WorkTree.FullName, "base", [], progress);

        GateSet set = await factory.CreateAsync(new RecipeChoice(Recipe.Reframe, ["src/"], "true"), Pin.Take(box.WorkTree.FullName), Ct);

        Assert.Empty(set.Gates);
        Assert.StartsWith("The public API of the base commit could not be listed, so there is no build:", set.Stop, StringComparison.Ordinal);
        Assert.Contains("no printer", set.Stop, StringComparison.Ordinal);
        Assert.Equal("api: start\n", progress.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task AListedBaseWritesTheTwoLinesOfTheApiGate()
    {
        await using var box = new GateSandbox();
        Sandbox sandbox = await box.StartAsync(Ct);
        using var progress = new StringWriter();
        var factory = new GateFactory(sandbox, new Git(new CommandRunner()), box.WorkTree.FullName, "base", [], progress);

        await factory.CreateAsync(new RecipeChoice(Recipe.Reframe, ["src/"], "true"), Pin.Take(box.WorkTree.FullName), Ct);

        Assert.Equal("api: start\napi: listed\n", progress.ToString().ReplaceLineEndings("\n"));
    }
}
