namespace Core;

/// <summary>A named, fixed sequence of phases, picked by a label of the issue.</summary>
public enum Recipe
{
    Behavior,
    Refactor,
    Cleanup,
    Reframe,
}

/// <summary>The kind of run that executes a recipe: the factory run from an issue, or the feedback run on its pull request.</summary>
public enum RunKind
{
    Factory,
    Feedback,
}

/// <summary>The recipe that the labels pick, with the scope and the check of the body. <paramref name="Declined"/> holds the
/// text for the issue's author when the labels or the body decline the issue, and is null otherwise.</summary>
public sealed record RecipeChoice(Recipe Recipe, IReadOnlyList<string> Scope, string? Check, string? Declined = null);

/// <summary>The words of a build prompt: the lead before the issue, and the instructions after it.</summary>
public sealed record BuildText(string Lead, string Instructions);

/// <summary>The words of the comment that stops a run whose check runs before the first phase: <paramref name="When"/> names that
/// phase, such as <c>before the plan</c>, and <paramref name="Work"/> names the work, such as <c>refactor</c>.</summary>
public sealed record PreCheckWords(string When, string Work);

/// <summary>Holds every rule that a recipe picks: the labels and the body it reads, its pin, and the texts of its prompts.</summary>
public static class RecipeRules
{
    private const string BehaviorLabel = "behavior";
    private const string RefactorLabel = "refactor";
    private const string CleanupLabel = "cleanup";
    private const string ReframeLabel = "reframe";
    private const string FeatureDirectory = "specs/features/";
    private const string PlanPath = ".factory/plan.md";

    private const string CannedAnswerText =
        "The build suite answers each call to a service with a canned answer from a stub HttpMessageHandler. ";

    private const string IntegrationText =
        CannedAnswerText
        + "For each canned answer that a scenario depends on, name in the plan one test under tests/Integration/ that gets the same answer from the real service. ";

    private const string ReframeBuildText = "Make the change the issue asks for to its config files first, then run dotnet format --no-restore once from the repo root, before any edit to a source file. Edit by hand only what the build still reports.";

    private const string ReframeReviewText = "Judge the build against every sentence of the issue: it changes only the paths under ## Scope, keeps the behavior of the program and makes the command under ## Check pass, and it keeps every test and every public type and member of the code under src/.";

    private const string BindText =
        "Bind every scenario of every feature file, in step classes under tests/Specs/Steps/. "
        + "Write unit tests for the new code under tests/Specs/Unit/. ";

    private const string PlanLead = "Plan the work of the issue below.";

    private const string ThreadScopeText = "A thread that asks for a change to a path outside the `## Scope` takes the status `disputed`, and its reply names the scope.";

    /// <summary>Reads the recipe from the labels, and the scope and the check from the body. Two labels that exclude each
    /// other, and a cleanup or a reframe with no scope or no check, decline the issue. On a decline, the recipe is the one
    /// that the first matching label names, in the order reframe, refactor, cleanup.</summary>
    public static RecipeChoice Parse(IReadOnlyList<string> labels, string body)
    {
        if (labels.Contains(ReframeLabel))
        {
            if (new[] { BehaviorLabel, CleanupLabel, RefactorLabel }.FirstOrDefault(labels.Contains) is string other)
            {
                return Decline(Recipe.Reframe, $"The labels reframe and {other} exclude each other: each one names a kind of run. Keep one of the two labels.");
            }

            return Scoped(Recipe.Reframe, ReframeLabel, body);
        }

        if (labels.Contains(RefactorLabel))
        {
            if (new[] { BehaviorLabel, CleanupLabel }.FirstOrDefault(labels.Contains) is string other)
            {
                return Decline(Recipe.Refactor, $"The labels refactor and {other} exclude each other: a refactor run leaves every test as it is, and a {other} run does not. Keep one of the two labels.");
            }

            return new RecipeChoice(Recipe.Refactor, [], CleanupBody.ReadCheck(body));
        }

        if (labels.Contains(CleanupLabel))
        {
            if (labels.Contains(BehaviorLabel))
            {
                return Decline(Recipe.Cleanup, "The labels cleanup and behavior exclude each other: a cleanup run has no plan, and a behavior run has one. Keep one of the two labels.");
            }

            return Scoped(Recipe.Cleanup, CleanupLabel, body);
        }

        return new RecipeChoice(Recipe.Behavior, [], null);
    }

    /// <summary>Records the pin of a recipe: every test file and the settings files for a refactor, and the feature files and the
    /// settings files for any other recipe.</summary>
    public static Pin TakePin(Recipe recipe, string workTree) =>
        recipe is Recipe.Refactor ? Pin.TakeRefactor(workTree) : Pin.Take(workTree);

    /// <summary>The words of the plan prompt and the gate of the plan phase. A behavior with no <paramref name="scenarios"/>
    /// writes feature files and a plan. A behavior with scenarios, and a refactor, write the plan alone. A cleanup and a reframe
    /// have no plan phase, so they throw.</summary>
    public static PlanText PlanText(Recipe recipe, IssueScenariosResult? scenarios = null) => recipe switch
    {
        Recipe.Behavior when scenarios is { } given => new(
            PlanLead,
            Named("The issue wrote these feature files, and the build makes their scenarios pass: ", given.Written)
            + Named("The issue removed these feature files: ", given.Removed)
            + $"Write the plan to {PlanPath}, stating each result the build owes. "
            + IntegrationText
            + $"Write no other file: {PlanPath} is the only file the plan phase writes. "
            + "Leave every other file as it is.",
            PlanMode.PlanOnly),
        Recipe.Behavior => new(
            PlanLead,
            $"Write Gherkin scenarios for the behavior in feature files under {FeatureDirectory} "
            + $"and write the plan to {PlanPath}, stating each result the build owes. "
            + IntegrationText
            + "Remove each feature file whose behavior the issue removes or replaces. "
            + "Leave every other file as it is.",
            PlanMode.FeaturesAndPlan),
        Recipe.Refactor => new(
            "Plan the work of the refactor issue below.",
            $"Write the plan to {PlanPath}, stating the structure the build gives the code and the steps to get there. "
            + $"Write no other file: {PlanPath} is the only file the plan phase writes. "
            + "Leave every other file as it is.",
            PlanMode.PlanOnly),
        _ => throw new ArgumentOutOfRangeException(nameof(recipe), recipe, "A cleanup and a reframe have no plan phase."),
    };

    /// <summary>The words of the build prompt. A feedback run has no plan, so no text of <see cref="RunKind.Feedback"/> names one.</summary>
    public static BuildText BuildText(Recipe recipe, RunKind run) => recipe switch
    {
        Recipe.Reframe => new(
            "Do the reframe of the issue below.",
            ReframeBuildText + " "
            + "Change only the paths that the issue names under the heading ## Scope. "
            + "Make the command in the first fenced block under the heading ## Check exit with the code 0. "
            + "Keep every test that passes now passing, and keep every public type and every public or protected member of the code under src/ as it is. "
            + "Leave each feature file under specs/features/ as it is. Run dotnet build and dotnet test, and work until both pass."),
        Recipe.Cleanup => new(
            "Do the cleanup of the issue below.",
            "Change only the paths that the issue names under the heading ## Scope. "
            + "Make the command in the first fenced block under the heading ## Check exit with the code 0, and keep every test that passes now passing. "
            + "Leave each feature file under specs/features/ as it is. Run dotnet build and dotnet test, and work until both pass."),
        Recipe.Refactor => new(
            "Do the refactor of the issue below.",
            (run is RunKind.Factory
                ? $"Read the plan, {PlanPath}, and change the structure of the code as it says. "
                : "Change the structure of the code as the issue says. ")
            + "Change no file under specs/features/, tests/Specs/ or tests/Integration/: every test stays as it is. "
            + "Leave each build settings file as it is; the build settings files are named "
            + SettingsFiles()
            + ". Run dotnet build and dotnet test, and work until both pass."),
        _ => new(
            "Build the work of the issue below.",
            BehaviorBuild(run)
            + "Leave each feature file and each build settings file as it is; the build settings files are named "
            + SettingsFiles()
            + ". Run dotnet build and dotnet test, and work until both pass."),
    };

    /// <summary>The numbered steps of the review prompt that come after "Your job:", each one ending with a line feed.</summary>
    public static string ReviewSteps(Recipe recipe, RunKind run) => recipe switch
    {
        Recipe.Reframe =>
            $"1. Read every feature file under {FeatureDirectory}.\n"
            + "2. " + ReframeReviewText + "\n",
        Recipe.Refactor =>
            (run is RunKind.Factory ? $"1. Read the plan, {PlanPath}.\n" : "1. Read every change since the base commit.\n")
            + (run is RunKind.Factory ? "2. Judge the build against the plan and every sentence of the issue. " : "2. Judge the build against every sentence of the issue. ")
            + "Block a change to any file under specs/features/, tests/Specs/ or tests/Integration/.\n",
        Recipe.Cleanup =>
            $"1. Read every feature file under {FeatureDirectory}.\n"
            + "2. Judge the build against every sentence of the issue: it changes only the paths under ## Scope and makes the command under ## Check pass, "
            + "and it keeps every test.\n",
        _ =>
            (run is RunKind.Factory
                ? $"1. Read every feature file under {FeatureDirectory} and the plan, {PlanPath}.\n"
                    + "2. Judge the build against every scenario, every result the plan states and every sentence of the issue, "
                : $"1. Read every feature file under {FeatureDirectory}.\n"
                    + "2. Judge the build against every scenario and every sentence of the issue, ")
            + "and judge whether the tests cover what each scenario names. "
            + CannedAnswerText
            + "For each canned answer that a scenario depends on, block a build with no test under tests/Integration/ that gets the same answer from the real service. "
            + "An existing test is a scenario under specs/features/, or a [Fact] or [Theory] method under tests/Specs/Unit/ or tests/Integration/, at the base commit. "
            + "Each row of the Examples of a Scenario Outline, and each [InlineData] row of a [Theory], is a test, and an edit to the row is an edit to that test. "
            + "A step class, a helper and a stub are not tests. "
            + "Block an edit to the text of an existing scenario or to the body of an existing test method, a rename or a removal, when the issue does not name that test. "
            + "A wrapper around the body, such as a retry, is an edit. "
            + "An edit to a step that an existing scenario uses keeps what the step does and what it asserts. "
            + "New steps, state of a step class moved into a shared class, and new helpers pass.\n",
    };

    /// <summary>The gates of a build of <paramref name="choice"/>, in the order they run, for both run kinds. The review runs after
    /// every gate passes, so it has no place in the list.</summary>
    public static IReadOnlyList<GateName> Gates(RecipeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return choice.Recipe switch
        {
            Recipe.Refactor when choice.Check is not null => [GateName.Test, GateName.Check],
            Recipe.Cleanup => [GateName.Scope, GateName.Test, GateName.Check],
            Recipe.Reframe => [GateName.Scope, GateName.Test, GateName.Api, GateName.Check],
            _ => [GateName.Test],
        };
    }

    /// <summary>The words of the comment that a check before the first phase stops the run with, for a recipe with a check.</summary>
    public static PreCheckWords PreCheck(Recipe recipe) => recipe switch
    {
        Recipe.Refactor => new("before the plan", "refactor"),
        Recipe.Cleanup => new("before the build", "clean up"),
        Recipe.Reframe => new("before the build", "reframe"),
        _ => throw new ArgumentOutOfRangeException(nameof(recipe), recipe, "A behavior has no check."),
    };

    /// <summary>The sentence of a feedback run on a thread that asks for a change outside the scope, for a cleanup and a
    /// reframe, or the empty string for a recipe with no scope.</summary>
    public static string ThreadRules(Recipe recipe) => recipe is Recipe.Cleanup or Recipe.Reframe ? ThreadScopeText : "";

    private static RecipeChoice Decline(Recipe recipe, string text) => new(recipe, [], null, text);

    private static RecipeChoice Scoped(Recipe recipe, string label, string body)
    {
        IReadOnlyList<string> scope = CleanupBody.ReadScope(body);
        if (scope.Count is 0)
        {
            return Decline(recipe, $"The body of a {label} issue names its scope under the heading ## Scope, one path for each line. The body holds no such heading with a path under it.");
        }

        if (CleanupBody.ReadCheck(body) is not string check)
        {
            return Decline(recipe, $"The body of a {label} issue holds its check command in the first fenced block under the heading ## Check. The body holds no such heading with a fenced block under it.");
        }

        return new RecipeChoice(recipe, scope, check);
    }

    private static string BehaviorBuild(RunKind run) => run is RunKind.Factory
        ? $"Read the plan, {PlanPath}, and each feature file under {FeatureDirectory}. "
            + "Write the code that the plan and the scenarios ask for. "
            + BindText
        : $"Read each feature file under {FeatureDirectory}. "
            + "Write the code that the issue and the scenarios ask for. "
            + BindText;

    private static string Named(string lead, IReadOnlyList<string> paths) =>
        paths.Count is 0 ? "" : lead + string.Join(", ", paths) + ". ";

    private static string SettingsFiles() => string.Join(", ", Pin.SettingsFileNames.Order(StringComparer.Ordinal));
}
