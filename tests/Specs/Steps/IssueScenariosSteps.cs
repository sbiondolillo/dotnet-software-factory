using System.Security.Cryptography;
using System.Text;
using Core;
using Reqnroll;
using Specs.Unit;
using Xunit;

namespace Specs.Steps;

[Binding]
public sealed class IssueScenariosSteps(FactoryRunSteps factory, PlanPhaseSteps plan)
{
    private const string Greeting = "specs/features/greeting.feature";
    private const string GreetingName = "specs/features/greeting-name.feature";

    private const string NameSpan =
        "  Scenario: A name gets a greeting\n    Given the name \"Ada\"\n    When the program greets\n    Then it prints \"Hello, Ada!\"\n";

    private const string WorldSpan =
        "  Scenario: No name gets the world\n    Given no name\n    When the program greets\n    Then it prints \"Hello, world!\"\n";

    private const string NewWorldSpan =
        "  Scenario: No name gets the world\n    Given an empty name\n    When the program greets\n    Then it prints \"Hello, world!\"\n";

    private const string LongSpan =
        "  Scenario: A long name is cut\n    Given a name of 100 letters\n    When the program greets\n    Then it prints the first 20 letters\n";

    private const string NewNameTagged =
        "  @hello\n  Scenario: A name gets a greeting\n    Given the name \"Bo\"\n    When the program greets\n    Then it prints \"Hello, Bo!\"\n";

    private const string NameOutline =
        "  Scenario Outline: A name gets a greeting\n    Given the name \"<name>\"\n    When the program greets\n    Then it prints \"Hello, <name>!\"\n\n"
        + "    Examples:\n      | name |\n      | Ada  |\n      | Bo   |\n";

    private const string WorldComment = "  # The world is the default name.\n";

    private const string GreetingText = "Feature: Greeting\n\n" + NameSpan + "\n" + WorldSpan;

    private const string TaggedGreetingText = "Feature: Greeting\n\n  @hello\n" + NameSpan + "\n" + WorldComment + "  @world\n" + WorldSpan;

    private const string OldExamples = "    @short\n    Examples:\n      | name |\n      | Ada  |\n";

    private const string NewExamples = "    @long\n    Examples:\n      | name        |\n      | Bartholomew |\n";

    private const string EachNameSteps =
        "  Scenario Outline: Each name gets a greeting\n    Given the name \"<name>\"\n    When the program greets\n    Then it prints \"Hello, <name>!\"\n\n";

    private const string GreetingNameText = "Feature: Greeting a name\n\n" + LongSpan;

    private const string LetterText =
        "Feature: Greeting a name\n\n  Scenario: A letter holds the greeting\n    Given the letter\n      ```\n      Hello, Ada!\n      ```\n"
        + "    When the program greets\n    Then it prints the letter\n";

    private readonly Dictionary<string, string> _main = new(StringComparer.Ordinal);
    private readonly List<string> _blocks = [];
    private readonly List<string> _removed = [];
    private string? _body;
    private string _blockText = "";
    private FactoryRunResult? _result;
    private string? _readmeSection;
    private string? _paragraph;

    private FactoryRunHarness H => factory.Harness;

    private FactoryRunResult Result => _result ?? throw new InvalidOperationException("the run gave no result");

    private IEnumerable<SeenPrompt> PlanPrompts => H.Prompts.Where(p => p.Kind is "plan");

    private static string Block(string path, string text) => $"```gherkin\n# {path}\n{text}```\n";

    private static string Removed(string path, string name) => $"- `{path}`: {name}";

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private string Read(string path) => File.ReadAllText(Path.Combine(H.CloneDirectory, path));

    private void Main(string path, string text) => _main[path] = text;

    private void Scenarios(string path, string text) => _blocks.Add(Block(path, text));

    private void Whole(string path, string text)
    {
        _blockText = text;
        Scenarios(path, text);
    }

    private string Compose()
    {
        var body = new StringBuilder();
        if (_blocks.Count > 0)
        {
            body.Append("## Scenarios\n\n").AppendJoin("\n", _blocks);
        }

        if (_removed.Count > 0)
        {
            body.Append("\n## Removed scenarios\n\n").AppendJoin("\n", _removed).Append('\n');
        }

        return body.ToString();
    }

    [Given("an issue whose ## Scenarios holds a block with a Feature: line for {string}")]
    public void GivenWholeFile(string path) =>
        Whole(path, path is GreetingName ? GreetingNameText : "Feature: Greeting\n\n" + LongSpan);

    [Given("the clone holds {string}")]
    public void GivenCloneHolds(string path)
    {
        Assert.Equal(Greeting, path);
        Main(path, GreetingText);
    }

    [Given("the clone holds {string} with the scenarios {string} and {string}")]
    public void GivenCloneScenarios(string path, string first, string second)
    {
        Assert.Equal([Greeting, "A name gets a greeting", "No name gets the world"], [path, first, second]);
        Main(path, GreetingText);
    }

    [Given("an issue whose block for {string} holds a new text of {string} and the scenario {string}")]
    public void GivenNewTextAndScenario(string path, string replaced, string added)
    {
        Assert.Equal(["No name gets the world", "A long name is cut"], [replaced, added]);
        Scenarios(path, NewWorldSpan + "\n" + LongSpan);
    }

    [Given("the clone holds {string}, which ends in a blank line")]
    public void GivenCloneBlankEnd(string path) => Main(path, GreetingText + "\n");

    [Given("an issue whose block for {string} holds the scenario {string}")]
    public void GivenBlockScenario(string path, string name)
    {
        Assert.Equal("A long name is cut", name);
        Scenarios(path, LongSpan);
    }

    [Given("the clone holds {string} with the tagged scenarios {string} and {string}, with a blank line and a comment line above the tag of {string}")]
    public void GivenCloneTaggedComment(string path, string first, string second, string commented)
    {
        Assert.Equal(["A name gets a greeting", "No name gets the world", "No name gets the world"], [first, second, commented]);
        Main(path, TaggedGreetingText);
    }

    [Given("an issue whose block for {string} holds a new text of {string} with its tag")]
    public void GivenBlockTagged(string path, string name)
    {
        Assert.Equal("A name gets a greeting", name);
        Scenarios(path, NewNameTagged);
    }

    [Given("an issue whose block for {string} holds {string} with an Examples table")]
    public void GivenBlockOutline(string path, string line)
    {
        Assert.Equal("Scenario Outline: A name gets a greeting", line);
        Scenarios(path, NameOutline);
    }

    [Given("the clone holds a scenario outline {string} with a tagged Examples table, and a scenario after it")]
    public void GivenCloneOutline(string name)
    {
        Assert.Equal("Each name gets a greeting", name);
        Main(Greeting, "Feature: Greeting\n\n" + EachNameSteps + OldExamples + "\n" + WorldSpan);
    }

    [Given("an issue whose block holds a new text of {string} with a new Examples table")]
    public void GivenBlockNewExamples(string name)
    {
        Assert.Equal("Each name gets a greeting", name);
        Scenarios(Greeting, EachNameSteps + NewExamples);
    }

    [Given("the clone holds {string} with the tagged scenarios {string} and {string}")]
    public void GivenCloneTagged(string path, string first, string second)
    {
        Assert.Equal(["No name gets the world", "A name gets a greeting"], [first, second]);
        Main(path, "Feature: Greeting\n\n  @world\n" + WorldSpan + "\n  @hello\n" + NameSpan);
    }

    [Given("an issue whose ## Removed scenarios lists {string}")]
    public void GivenRemovedLists(string item) => _removed.Add("- " + item);

    [Given("the clone holds {string} with one scenario, and {string}")]
    public void GivenCloneFarewell(string farewell, string greeting)
    {
        Main(farewell, "Feature: Farewell\n\n  Scenario: A name gets a farewell\n    Given the name \"Ada\"\n    When the program says goodbye\n    Then it prints \"Goodbye, Ada!\"\n");
        Main(greeting, GreetingText);
    }

    [Given("an issue whose ## Removed scenarios lists that one scenario")]
    public void GivenRemovedFarewell() => _removed.Add(Removed("specs/features/farewell.feature", "A name gets a farewell"));

    [Given("an issue with ## Removed scenarios and no ## Scenarios")]
    public void GivenRemovedOnly()
    {
        Main(Greeting, GreetingText);
        _removed.Add(Removed(Greeting, "No name gets the world"));
    }

    [Given("a block whose step holds a doc string fenced with ``` and indented 6 spaces")]
    public void GivenDocStringBlock() => Whole(GreetingName, LetterText);

    [Given("an issue with ## Scenarios")]
    public void GivenIssueWithScenarios()
    {
        Main(Greeting, GreetingText);
        Whole(GreetingName, GreetingNameText);
        Scenarios(Greeting, LongSpan);
    }

    [Given(@"^an issue body with (.+)$")]
    public void GivenBody(string fault)
    {
        Main(Greeting, GreetingText);
        switch (fault)
        {
            case "CRLF line ends and a block for \"specs/features/greeting-name.feature\"":
                Whole(GreetingName, GreetingNameText);
                _body = Compose().Replace("\n", "\r\n", StringComparison.Ordinal);
                break;
            case "a ## Scenarios heading with no fenced block":
                _body = "## Scenarios\n\nThe scenarios follow.\n";
                break;
            case "a block with no closing fence":
                _body = "## Scenarios\n\n```gherkin\n# " + Greeting + "\n" + LongSpan;
                break;
            case "a block whose first line is \"Feature: Greeting\"":
                _body = "## Scenarios\n\n```gherkin\nFeature: Greeting\n\n" + LongSpan + "```\n";
                break;
            case "a block whose first line is \"# specs/features/../README.feature\"":
                Scenarios("specs/features/../README.feature", LongSpan);
                break;
            case "two blocks for \"specs/features/greeting.feature\"":
                Scenarios(Greeting, LongSpan);
                Scenarios(Greeting, NewWorldSpan);
                break;
            case "a block with no Feature: line for \"specs/features/new.feature\", which the clone lacks":
                Scenarios("specs/features/new.feature", LongSpan);
                break;
            case "a block with no Feature: line that holds text before its first scenario":
                Scenarios(Greeting, "  The block starts with words.\n\n" + LongSpan);
                break;
            case "a block with no Feature: line that holds a Background: or a Rule:":
                Scenarios(Greeting, "  Background:\n    Given the program\n\n" + LongSpan);
                break;
            case "a block of scenarios or a removed scenario for a file on main that holds a Rule:":
                Main(Greeting, "Feature: Greeting\n\n  Rule: Names\n\n    Scenario: A name gets a greeting\n      Given the name \"Ada\"\n      When the program greets\n      Then it prints \"Hello, Ada!\"\n");
                Scenarios(Greeting, LongSpan);
                break;
            case "a block that holds two scenarios named 'A long name is cut'":
                Scenarios(Greeting, LongSpan + "\n" + LongSpan);
                break;
            case "a scenario whose name the file holds twice":
                Main(Greeting, "Feature: Greeting\n\n" + NameSpan + "\n" + NameSpan);
                Scenarios(Greeting, NewNameTagged);
                break;
            case "a block for \"specs/features/greeting.feature\" that does not parse":
                Scenarios(Greeting, "  Scenario: A long name is cut\n    Given a name of 100 letters\n  Feature: Again\n");
                break;
            case "a block for \"specs/features/greeting.feature\" whose doc string text sits deeper than its opening delimiter":
                Scenarios(Greeting, "  Scenario: A letter holds the greeting\n    Given the letter\n      \"\"\"\n        Hello, Ada!\n      \"\"\"\n    Then it prints the letter\n");
                break;
            case "a block that gives the file the scenarios \"A name gets a greeting\" and \"a name, gets a greeting\"":
                Scenarios(Greeting, NameSpan.Replace("A name gets", "a name, gets", StringComparison.Ordinal));
                break;
            case "a ## Removed scenarios line \"specs/features/greeting.feature: No name gets the world\" with no backticks":
                _body = "## Removed scenarios\n\nspecs/features/greeting.feature: No name gets the world\n";
                break;
            case "a removed scenario in \"specs/features/gone.feature\", which the clone lacks":
                _removed.Add(Removed("specs/features/gone.feature", "A name gets a greeting"));
                break;
            case "a removed scenario \"A lost name\" that its file lacks":
                _removed.Add(Removed(Greeting, "A lost name"));
                break;
            case "a removed scenario whose name a block for the same file also holds":
                Scenarios(Greeting, NewWorldSpan);
                _removed.Add(Removed(Greeting, "No name gets the world"));
                break;
            case "a removed scenario in a file that a whole-file block replaces":
                Scenarios(Greeting, "Feature: Greeting\n\n" + LongSpan);
                _removed.Add(Removed(Greeting, "No name gets the world"));
                break;
            case "removals that leave no feature file":
                _removed.Add(Removed(Greeting, "A name gets a greeting"));
                _removed.Add(Removed(Greeting, "No name gets the world"));
                break;
            case "blocks and removals that leave every file as it is on main":
                Scenarios(Greeting, NameSpan);
                break;
            case "both ## Scenarios and ## Acceptance criteria":
                Scenarios(Greeting, LongSpan);
                _body = Compose() + "\n## Acceptance criteria\n\n- A long name is cut.\n";
                break;
            case "a block that starts \"# specs/features/greeting.feature\" outside ## Scenarios":
                Scenarios(Greeting, LongSpan);
                _body = Compose() + "\n## Notes\n\n" + Block(Greeting, LongSpan);
                break;
            case "a block with no closing fence and a removed scenario in a file that the clone lacks":
                _body = "## Removed scenarios\n\n" + Removed("specs/features/gone.feature", "A name gets a greeting") + "\n\n## Scenarios\n\n```gherkin\n# " + Greeting + "\n" + LongSpan;
                break;
            default:
                throw new InvalidOperationException($"no issue body for: {fault}");
        }
    }

    [When("the factory runs the issue")]
    [When("the plan phase runs")]
    public async Task WhenRuns()
    {
        H.Body = _body ?? Compose();
        H.AfterClone = directory =>
        {
            foreach ((string path, string text) in _main)
            {
                string full = Path.Combine(directory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }
        };
        plan.OtherPrompts = () => [.. PlanPrompts.Select(p => p.Text)];

        // The test gate asks for a passed test for each scenario of a pinned feature file.
        foreach (string name in new[] { "A name gets a greeting", "No name gets the world", "A long name is cut", "A letter holds the greeting", "Each name gets a greeting", "A name gets a farewell" })
        {
            H.AfterOnlyTests.Add(("Specs.Features.GreetingFeature", FeatureFiles.ComputeTestName(name)));
        }

        _result = await H.RunAsync(TestContext.Current.CancellationToken);
    }

    [When("the plan agent changes {string} on the first prompt")]
    public Task WhenPlanChanges(string path)
    {
        H.PlanChanges.Add(path);
        return WhenRuns();
    }

    [When("the plan agent adds {string} on the first prompt")]
    public Task WhenPlanAdds(string path)
    {
        H.PlanExtraFiles.Add(path);
        return WhenRuns();
    }

    // The test gate compares the work tree with the pin after each build, and the build in this harness leaves every
    // feature file as it is. So a delivered pull request shows that the pin holds the work tree's feature files.
    private void AssertDeliveredAgainstThePin() => Assert.NotNull(Result.PullRequest);

    [Then("the pin holds {string} with the hash of the block's text after its first line")]
    public void ThenPinHash(string path)
    {
        Assert.Equal(Hash(_blockText), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(H.CloneDirectory, path)))));
        AssertDeliveredAgainstThePin();
    }

    [Then("{string} ends with its text on main, one blank line, and the scenario {string}")]
    public void ThenEndsWith(string path, string name)
    {
        Assert.Equal("A long name is cut", name);
        Assert.Equal("Feature: Greeting\n\n" + NameSpan + "\n" + NewWorldSpan + "\n" + LongSpan, Read(path));
    }

    [Then("{string} is replaced in place")]
    public void ThenReplacedInPlace(string name)
    {
        Assert.Equal("No name gets the world", name);
        string text = Read(Greeting);
        Assert.DoesNotContain(WorldSpan, text, StringComparison.Ordinal);
        Assert.Contains(NameSpan + "\n" + NewWorldSpan, text, StringComparison.Ordinal);
    }

    [Then("{string} holds exactly one blank line before {string}")]
    public void ThenOneBlankLine(string path, string name)
    {
        string[] lines = Read(path).Split('\n');
        int at = Array.FindIndex(lines, line => line.Trim() == $"Scenario: {name}");
        Assert.True(at >= 2);
        Assert.Equal("", lines[at - 1].Trim());
        Assert.NotEqual("", lines[at - 2].Trim());
    }

    [Then("{string} is its text on main with the span of {string} replaced by the block's scenario")]
    public void ThenSpanReplaced(string path, string name)
    {
        Assert.Equal("A name gets a greeting", name);
        Assert.Equal("Feature: Greeting\n\n" + NewNameTagged + "\n" + WorldComment + "  @world\n" + WorldSpan, Read(path));
    }

    [Then("the blank line, the comment, the tag and the text of {string} are as they are on main")]
    public void ThenRestAsOnMain(string name)
    {
        Assert.Equal("No name gets the world", name);
        string onMain = TaggedGreetingText[TaggedGreetingText.IndexOf(NameSpan, StringComparison.Ordinal)..][NameSpan.Length..];
        Assert.EndsWith(onMain, Read(Greeting), StringComparison.Ordinal);
    }

    [Then("{string} is its text on main with the span of {string} replaced by the block's scenario outline")]
    public void ThenOutlineReplaced(string path, string name)
    {
        Assert.Equal("A name gets a greeting", name);
        Assert.Equal("Feature: Greeting\n\n" + NameOutline + "\n" + WorldSpan, Read(path));
    }

    [Then("the file holds the new Examples table and not the old one")]
    public void ThenNewExamples()
    {
        string text = Read(Greeting);
        Assert.Contains(NewExamples, text, StringComparison.Ordinal);
        Assert.DoesNotContain("@short", text, StringComparison.Ordinal);
        Assert.DoesNotContain("| Ada  |", text, StringComparison.Ordinal);
    }

    [Then("the scenario after it is as it is on main")]
    public void ThenAfterAsOnMain() =>
        Assert.Equal("Feature: Greeting\n\n" + EachNameSteps + NewExamples + "\n" + WorldSpan, Read(Greeting));

    [Then("{string} is its text on main without the span of {string} and the blank lines after it")]
    public void ThenSpanRemoved(string path, string name)
    {
        Assert.Equal("No name gets the world", name);
        Assert.Equal("Feature: Greeting\n\n  @hello\n" + NameSpan, Read(path));
    }

    [Then("{string} has its own tag alone")]
    public void ThenOwnTag(string name)
    {
        string[] lines = Read(Greeting).Split('\n');
        int at = Array.FindIndex(lines, line => line.Trim() == $"Scenario: {name}");
        Assert.Equal("@hello", lines[at - 1].Trim());
        Assert.DoesNotContain(lines, line => line.Trim() is "@world");
    }

    [Then("the work tree lacks {string}")]
    public void ThenTreeLacks(string path) => Assert.False(File.Exists(Path.Combine(H.CloneDirectory, path)));

    [Then("the pin lacks {string}")]
    public void ThenPinLacks(string path)
    {
        ThenTreeLacks(path);
        AssertDeliveredAgainstThePin();
    }

    [Then("the first plan prompt holds {string}")]
    public void ThenFirstPlanHolds(string text) => Assert.Contains(text, PlanPrompts.First().Text, StringComparison.Ordinal);

    [Then("{string} holds no carriage return")]
    public void ThenNoCarriageReturn(string path)
    {
        Assert.DoesNotContain('\r', Read(path));
        Assert.Equal(GreetingNameText, Read(path));
    }

    [Then("the feature file holds the doc string and every step after it")]
    public void ThenDocString() => Assert.Equal(LetterText, Read(GreetingName));

    [Then("the first prompt names each feature file that the issue wrote")]
    public void ThenNamesFiles()
    {
        string prompt = PlanPrompts.First().Text;
        Assert.Contains(Greeting, prompt, StringComparison.Ordinal);
        Assert.Contains(GreetingName, prompt, StringComparison.Ordinal);
    }

    [Then("the first prompt lacks {string}")]
    public void ThenFirstLacks(string text) => Assert.DoesNotContain(text, PlanPrompts.First().Text, StringComparison.Ordinal);

    [Then("the second prompt holds {string}")]
    public void ThenSecondHolds(string text) => Assert.Contains(text, PlanPrompts.ElementAt(1).Text, StringComparison.Ordinal);

    [Then("the issue gets a comment that holds {string}")]
    public void ThenComment(string text) => Assert.Contains(text, Assert.Single(H.CommentBodies), StringComparison.Ordinal);

    [Then("the progress lines end with {string}")]
    public void ThenProgressEnds(string line) =>
        Assert.Equal(line, H.Progress.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Last(l => l.Length > 0));

    [Then("the run gives back no pull request and {int} Redos")]
    public void ThenNoPull(int redos)
    {
        Assert.Null(Result.PullRequest);
        Assert.Equal(redos, Result.Redos);
        Assert.Empty(H.Prompts);
    }

    [Then("the issue gets one comment that holds both faults")]
    public void ThenBothFaults()
    {
        string body = Assert.Single(H.CommentBodies);
        Assert.Contains("A fenced block under the heading ## Scenarios has no closing fence.", body, StringComparison.Ordinal);
        Assert.Contains("The file specs/features/gone.feature, under ## Removed scenarios, is not on main.", body, StringComparison.Ordinal);
    }

    [Given("the README section {string}")]
    public void GivenReadmeSection(string heading) =>
        _readmeSection = WorkflowChecks.Section(Path.Combine(StarterChecks.RepoRoot(), "README.md"), heading);

    [Then("it holds a paragraph after the line that starts {string}")]
    public void ThenParagraphAfter(string start)
    {
        string[] paragraphs = (_readmeSection ?? "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        int at = Array.FindIndex(paragraphs, p => p.StartsWith(start, StringComparison.Ordinal));
        Assert.True(at >= 0 && at + 1 < paragraphs.Length);
        _paragraph = paragraphs[at + 1];
    }

    [Then("the paragraph names `## Scenarios`, `## Removed scenarios`, the whole-file block and the upsert")]
    public void ThenParagraphNames()
    {
        foreach (string text in new[] { "`## Scenarios`", "`## Removed scenarios`", "whole-file block", "upsert" })
        {
            Assert.Contains(text, _paragraph, StringComparison.Ordinal);
        }
    }

    [Then("the section holds no new line that starts {string}")]
    public void ThenNoNewHeading(string start)
    {
        Assert.NotNull(_paragraph);
        Assert.DoesNotContain(_paragraph.Split('\n'), line => line.StartsWith(start, StringComparison.Ordinal));
        Assert.Contains("Create the label `review-feedback` in the new repo.", _readmeSection, StringComparison.Ordinal);
    }
}
