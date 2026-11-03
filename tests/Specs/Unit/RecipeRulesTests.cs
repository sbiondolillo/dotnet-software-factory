using Core;
using Xunit;

namespace Specs.Unit;

public sealed class RecipeRulesTests
{
    private const string Body = "## Scope\nREADME.md\n\n## Check\n\n```sh\ntrue\n```\n";

    [Theory]
    [InlineData("cleanup", "reframe", Recipe.Reframe, "The labels reframe and cleanup exclude each other: each one names a kind of run. Keep one of the two labels.")]
    [InlineData("behavior", "reframe", Recipe.Reframe, "The labels reframe and behavior exclude each other: each one names a kind of run. Keep one of the two labels.")]
    [InlineData("refactor", "reframe", Recipe.Reframe, "The labels reframe and refactor exclude each other: each one names a kind of run. Keep one of the two labels.")]
    [InlineData("behavior", "refactor", Recipe.Refactor, "The labels refactor and behavior exclude each other: a refactor run leaves every test as it is, and a behavior run does not. Keep one of the two labels.")]
    [InlineData("cleanup", "refactor", Recipe.Refactor, "The labels refactor and cleanup exclude each other: a refactor run leaves every test as it is, and a cleanup run does not. Keep one of the two labels.")]
    [InlineData("behavior", "cleanup", Recipe.Cleanup, "The labels cleanup and behavior exclude each other: a cleanup run has no plan, and a behavior run has one. Keep one of the two labels.")]
    public void TwoLabelsThatExcludeEachOtherDecline(string first, string second, Recipe recipe, string text)
    {
        Assert.Equal(new RecipeChoice(recipe, [], null, text), RecipeRules.Parse([first, second], Body));
        Assert.Equal(new RecipeChoice(recipe, [], null, text), RecipeRules.Parse([second, first], Body));
    }

    [Theory]
    [InlineData(Recipe.Reframe, "The labels reframe and behavior exclude each other: each one names a kind of run. Keep one of the two labels.", "reframe", "refactor", "cleanup", "behavior")]
    [InlineData(Recipe.Refactor, "The labels refactor and behavior exclude each other: a refactor run leaves every test as it is, and a behavior run does not. Keep one of the two labels.", "cleanup", "refactor", "behavior")]
    public void ThreeLabelsNameTheFirstExcludedLabelInAFixedOrder(Recipe recipe, string text, params string[] labels) =>
        Assert.Equal(new RecipeChoice(recipe, [], null, text), RecipeRules.Parse(labels, Body));

    [Theory]
    [InlineData("cleanup", Recipe.Cleanup, "Fix the install test.", "The body of a cleanup issue names its scope under the heading ## Scope, one path for each line. The body holds no such heading with a path under it.")]
    [InlineData("reframe", Recipe.Reframe, "Fix the install test.", "The body of a reframe issue names its scope under the heading ## Scope, one path for each line. The body holds no such heading with a path under it.")]
    [InlineData("cleanup", Recipe.Cleanup, "## Scope\nREADME.md\n", "The body of a cleanup issue holds its check command in the first fenced block under the heading ## Check. The body holds no such heading with a fenced block under it.")]
    [InlineData("reframe", Recipe.Reframe, "## Scope\nREADME.md\n", "The body of a reframe issue holds its check command in the first fenced block under the heading ## Check. The body holds no such heading with a fenced block under it.")]
    public void AScopedRecipeWithNoScopeOrNoCheckDeclines(string label, Recipe recipe, string body, string text) =>
        Assert.Equal(new RecipeChoice(recipe, [], null, text), RecipeRules.Parse([label], body));

    [Theory]
    [InlineData("cleanup", Recipe.Cleanup)]
    [InlineData("reframe", Recipe.Reframe)]
    public void AScopedRecipeReadsItsScopeAndItsCheck(string label, Recipe recipe)
    {
        RecipeChoice choice = RecipeRules.Parse(["factory", label], Body);
        Assert.Equal(recipe, choice.Recipe);
        Assert.Equal(["README.md"], choice.Scope);
        Assert.Equal("true", choice.Check);
        Assert.Null(choice.Declined);
    }

    [Fact]
    public void ARefactorReadsItsCheckAndNoScope()
    {
        RecipeChoice choice = RecipeRules.Parse(["refactor"], Body);
        Assert.Equal(Recipe.Refactor, choice.Recipe);
        Assert.Empty(choice.Scope);
        Assert.Equal("true", choice.Check);
        Assert.Null(choice.Declined);
    }

    [Fact]
    public void ARefactorWithNoCheckLeavesTheCheckNull() =>
        Assert.Equal(new RecipeChoice(Recipe.Refactor, [], null), RecipeRules.Parse(["refactor"], "Rename Greeting to Greeter."));

    [Theory]
    [InlineData]
    [InlineData("behavior")]
    [InlineData("factory", "ready")]
    public void AnyOtherLabelsAreABehaviorThatReadsNoScopeAndNoCheck(params string[] labels) =>
        Assert.Equal(new RecipeChoice(Recipe.Behavior, [], null), RecipeRules.Parse(labels, Body));

    [Fact]
    public void ARefactorTakesTheWidePinAndEveryOtherRecipeTheNarrowPin()
    {
        DirectoryInfo work = Directory.CreateTempSubdirectory("recipe-pin-");
        try
        {
            Directory.CreateDirectory(Path.Combine(work.FullName, "tests", "Specs"));
            File.WriteAllText(Path.Combine(work.FullName, "tests", "Specs", "GreeterTests.cs"), "class GreeterTests;");
            Assert.Contains("tests/Specs/GreeterTests.cs", RecipeRules.TakePin(Recipe.Refactor, work.FullName).Hashes.Keys);
            foreach (Recipe recipe in new[] { Recipe.Behavior, Recipe.Cleanup, Recipe.Reframe })
            {
                Assert.DoesNotContain("tests/Specs/GreeterTests.cs", RecipeRules.TakePin(recipe, work.FullName).Hashes.Keys);
            }
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    [Fact]
    public void ABehaviorWithNoScenariosWritesFeatureFilesAndAPlan()
    {
        PlanText text = RecipeRules.PlanText(Recipe.Behavior);
        Assert.Equal("Plan the work of the issue below.", text.Lead);
        Assert.Equal(PlanMode.FeaturesAndPlan, text.Mode);
        Assert.StartsWith("Write Gherkin scenarios for the behavior in feature files under specs/features/ and write the plan to .factory/plan.md", text.Instructions, StringComparison.Ordinal);
        Assert.Contains("Remove each feature file whose behavior the issue removes or replaces.", text.Instructions, StringComparison.Ordinal);
        Assert.Contains("name in the plan one test under tests/Integration/", text.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void ABehaviorWithScenariosWritesThePlanAloneAndNamesTheFiles()
    {
        PlanText text = RecipeRules.PlanText(Recipe.Behavior, new IssueScenariosResult([], ["specs/features/a.feature"], ["specs/features/gone.feature"]));
        Assert.Equal("Plan the work of the issue below.", text.Lead);
        Assert.Equal(PlanMode.PlanOnly, text.Mode);
        Assert.StartsWith(
            "The issue wrote these feature files, and the build makes their scenarios pass: specs/features/a.feature. The issue removed these feature files: specs/features/gone.feature. Write the plan to .factory/plan.md",
            text.Instructions,
            StringComparison.Ordinal);
        Assert.Contains("Write no other file: .factory/plan.md is the only file the plan phase writes.", text.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefactorWritesThePlanAlone()
    {
        PlanText text = RecipeRules.PlanText(Recipe.Refactor);
        Assert.Equal("Plan the work of the refactor issue below.", text.Lead);
        Assert.Equal(PlanMode.PlanOnly, text.Mode);
        Assert.Equal(
            "Write the plan to .factory/plan.md, stating the structure the build gives the code and the steps to get there. Write no other file: .factory/plan.md is the only file the plan phase writes. Leave every other file as it is.",
            text.Instructions);
    }

    [Theory]
    [InlineData(Recipe.Cleanup)]
    [InlineData(Recipe.Reframe)]
    public void ACleanupAndAReframeHaveNoPlanText(Recipe recipe) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeRules.PlanText(recipe));

    [Theory]
    [InlineData(Recipe.Behavior, "Build the work of the issue below.")]
    [InlineData(Recipe.Refactor, "Do the refactor of the issue below.")]
    [InlineData(Recipe.Cleanup, "Do the cleanup of the issue below.")]
    [InlineData(Recipe.Reframe, "Do the reframe of the issue below.")]
    public void EachRecipeHasItsBuildLeadOnEachRunKind(Recipe recipe, string lead)
    {
        Assert.Equal(lead, RecipeRules.BuildText(recipe, RunKind.Factory).Lead);
        Assert.Equal(lead, RecipeRules.BuildText(recipe, RunKind.Feedback).Lead);
    }

    [Fact]
    public void ABehaviorBuildOfAFactoryRunReadsThePlanAndAsksForBindingsAndUnitTests()
    {
        string text = RecipeRules.BuildText(Recipe.Behavior, RunKind.Factory).Instructions;
        Assert.StartsWith("Read the plan, .factory/plan.md, and each feature file under specs/features/. Write the code that the plan and the scenarios ask for. ", text, StringComparison.Ordinal);
        Assert.Contains("Bind every scenario of every feature file, in step classes under tests/Specs/Steps/.", text, StringComparison.Ordinal);
        Assert.Contains("Write unit tests for the new code under tests/Specs/Unit/.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABehaviorBuildOfAFeedbackRunReadsTheFeatureFilesAndAsksForBindingsAndUnitTests()
    {
        string text = RecipeRules.BuildText(Recipe.Behavior, RunKind.Feedback).Instructions;
        Assert.StartsWith("Read each feature file under specs/features/. Write the code that the issue and the scenarios ask for. ", text, StringComparison.Ordinal);
        Assert.Contains("Bind every scenario of every feature file", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Recipe.Behavior)]
    [InlineData(Recipe.Refactor)]
    public void TheBuildOfARecipeWithTestsNamesEverySettingsFileAndRunsBuildAndTest(Recipe recipe)
    {
        foreach (RunKind run in new[] { RunKind.Factory, RunKind.Feedback })
        {
            string text = RecipeRules.BuildText(recipe, run).Instructions;
            foreach (string name in Pin.SettingsFileNames)
            {
                Assert.Contains(name, text, StringComparison.Ordinal);
            }

            Assert.EndsWith("Run dotnet build and dotnet test, and work until both pass.", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(RunKind.Factory, "Read the plan, .factory/plan.md, and change the structure of the code as it says. ")]
    [InlineData(RunKind.Feedback, "Change the structure of the code as the issue says. ")]
    public void ARefactorBuildKeepsEveryTestFile(RunKind run, string start)
    {
        string text = RecipeRules.BuildText(Recipe.Refactor, run).Instructions;
        Assert.StartsWith(start + "Change no file under specs/features/, tests/Specs/ or tests/Integration/: every test stays as it is. ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Recipe.Cleanup)]
    [InlineData(Recipe.Reframe)]
    public void AScopedBuildIsTheSameOnEachRunKindAndKeepsToTheScopeAndTheCheck(Recipe recipe)
    {
        BuildText text = RecipeRules.BuildText(recipe, RunKind.Factory);
        Assert.Equal(text, RecipeRules.BuildText(recipe, RunKind.Feedback));
        Assert.Contains("Change only the paths that the issue names under the heading ## Scope. ", text.Instructions, StringComparison.Ordinal);
        Assert.Contains("Make the command in the first fenced block under the heading ## Check exit with the code 0", text.Instructions, StringComparison.Ordinal);
        Assert.Contains("keep every test that passes now passing", text.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Leave each feature file under specs/features/ as it is. Run dotnet build and dotnet test, and work until both pass.", text.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void AReframeBuildStatesTheOrderOfWorkAndKeepsThePublicApi()
    {
        string text = RecipeRules.BuildText(Recipe.Reframe, RunKind.Factory).Instructions;
        Assert.StartsWith("Make the change the issue asks for to its config files first, then run dotnet format --no-restore once from the repo root, before any edit to a source file. Edit by hand only what the build still reports. ", text, StringComparison.Ordinal);
        Assert.Contains("keep every public type and every public or protected member of the code under src/ as it is.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFeedbackTextNamesThePlan()
    {
        foreach (Recipe recipe in Enum.GetValues<Recipe>())
        {
            Assert.DoesNotContain(".factory/plan.md", RecipeRules.BuildText(recipe, RunKind.Feedback).Lead, StringComparison.Ordinal);
            Assert.DoesNotContain(".factory/plan.md", RecipeRules.BuildText(recipe, RunKind.Feedback).Instructions, StringComparison.Ordinal);
            Assert.DoesNotContain(".factory/plan.md", RecipeRules.ReviewSteps(recipe, RunKind.Feedback), StringComparison.Ordinal);
            Assert.DoesNotContain(".factory/plan.md", RecipeRules.ThreadRules(recipe), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(Recipe.Behavior)]
    [InlineData(Recipe.Refactor)]
    public void AFactoryTextOfARecipeWithAPlanNamesThePlan(Recipe recipe)
    {
        Assert.Contains(".factory/plan.md", RecipeRules.BuildText(recipe, RunKind.Factory).Instructions, StringComparison.Ordinal);
        Assert.Contains(".factory/plan.md", RecipeRules.ReviewSteps(recipe, RunKind.Factory), StringComparison.Ordinal);
    }

    [Fact]
    public void ABehaviorReviewJudgesEachScenarioAndBlocksAnEditToAnExistingTest()
    {
        foreach (RunKind run in new[] { RunKind.Factory, RunKind.Feedback })
        {
            string steps = RecipeRules.ReviewSteps(Recipe.Behavior, run);
            Assert.StartsWith("1. Read every feature file under specs/features/", steps, StringComparison.Ordinal);
            Assert.Contains("and judge whether the tests cover what each scenario names. ", steps, StringComparison.Ordinal);
            Assert.Contains("The build suite answers each call to a service with a canned answer from a stub HttpMessageHandler. ", steps, StringComparison.Ordinal);
            Assert.Contains("Block an edit to the text of an existing scenario or to the body of an existing test method", steps, StringComparison.Ordinal);
            Assert.EndsWith("New steps, state of a step class moved into a shared class, and new helpers pass.\n", steps, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(RunKind.Factory, "1. Read every feature file under specs/features/ and the plan, .factory/plan.md.\n2. Judge the build against every scenario, every result the plan states and every sentence of the issue, ")]
    [InlineData(RunKind.Feedback, "1. Read every feature file under specs/features/.\n2. Judge the build against every scenario and every sentence of the issue, ")]
    public void ABehaviorReviewReadsThePlanOnlyInAFactoryRun(RunKind run, string start) =>
        Assert.StartsWith(start, RecipeRules.ReviewSteps(Recipe.Behavior, run), StringComparison.Ordinal);

    [Theory]
    [InlineData(RunKind.Factory, "1. Read the plan, .factory/plan.md.\n2. Judge the build against the plan and every sentence of the issue. ")]
    [InlineData(RunKind.Feedback, "1. Read every change since the base commit.\n2. Judge the build against every sentence of the issue. ")]
    public void ARefactorReviewBlocksAChangeToATestFile(RunKind run, string start) =>
        Assert.Equal(start + "Block a change to any file under specs/features/, tests/Specs/ or tests/Integration/.\n", RecipeRules.ReviewSteps(Recipe.Refactor, run));

    [Fact]
    public void ACleanupReviewJudgesTheScopeTheCheckAndTheTests()
    {
        string steps = RecipeRules.ReviewSteps(Recipe.Cleanup, RunKind.Factory);
        Assert.Equal(steps, RecipeRules.ReviewSteps(Recipe.Cleanup, RunKind.Feedback));
        Assert.Equal(
            "1. Read every feature file under specs/features/.\n2. Judge the build against every sentence of the issue: it changes only the paths under ## Scope and makes the command under ## Check pass, and it keeps every test.\n",
            steps);
    }

    [Fact]
    public void AReframeReviewAlsoJudgesTheBehaviorAndThePublicApi()
    {
        string steps = RecipeRules.ReviewSteps(Recipe.Reframe, RunKind.Factory);
        Assert.Equal(steps, RecipeRules.ReviewSteps(Recipe.Reframe, RunKind.Feedback));
        Assert.Equal(
            "1. Read every feature file under specs/features/.\n2. Judge the build against every sentence of the issue: it changes only the paths under ## Scope, keeps the behavior of the program and makes the command under ## Check pass, and it keeps every test and every public type and member of the code under src/.\n",
            steps);
    }

    [Theory]
    [InlineData(Recipe.Cleanup)]
    [InlineData(Recipe.Reframe)]
    public void AScopedRecipeDisputesAThreadOutsideTheScope(Recipe recipe) =>
        Assert.Equal("A thread that asks for a change to a path outside the `## Scope` takes the status `disputed`, and its reply names the scope.", RecipeRules.ThreadRules(recipe));

    [Theory]
    [InlineData(Recipe.Behavior)]
    [InlineData(Recipe.Refactor)]
    public void ARecipeWithNoScopeHasNoThreadRule(Recipe recipe) => Assert.Equal("", RecipeRules.ThreadRules(recipe));

    public static TheoryData<RecipeChoice, GateName[]> GateLists() => new()
    {
        { new RecipeChoice(Recipe.Behavior, [], null), [GateName.Test] },
        { new RecipeChoice(Recipe.Refactor, [], null), [GateName.Test] },
        { new RecipeChoice(Recipe.Refactor, [], "true"), [GateName.Test, GateName.Check] },
        { new RecipeChoice(Recipe.Cleanup, ["README.md"], "true"), [GateName.Scope, GateName.Test, GateName.Check] },
        { new RecipeChoice(Recipe.Reframe, ["src/"], "true"), [GateName.Scope, GateName.Test, GateName.Api, GateName.Check] },
    };

    [Theory]
    [MemberData(nameof(GateLists))]
    public void EachChoiceGetsItsGatesInOrder(RecipeChoice choice, GateName[] gates) => Assert.Equal(gates, RecipeRules.Gates(choice));
}
