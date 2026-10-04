using Xunit;

namespace Specs.Unit;

public sealed class WorkflowChecksTests : IDisposable
{
    private const string Run = "dotnet run --project src/App -c Release -- run --repo ${{ github.repository }} --issue ${{ inputs.issue || github.event.issue.number }} --pass-env EXAMPLE_API_KEY";
    private const string Condition = "github.event_name == 'workflow_dispatch' || (github.event.label.name == 'factory' && github.event.sender.login == github.repository_owner)";
    private const string Dispatch = "workflow_dispatch:\n    inputs:\n      issue:\n        required: true\n        type: number\n      ref:\n        default: v0";
    private const string Heading = "## Start a new repo";
    private const string Second = "dotnet new factory-starter -o <directory>";

    private readonly string _root = Directory.CreateTempSubdirectory("workflow-checks-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private string Write(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Factory(string on = "issues:\n    types: [labeled]\n  " + Dispatch, string permissions = "permissions: {}", string runsOn = "ubuntu-latest",
        string condition = Condition, string checkout = "repository: sbiondolillo/dotnet-software-factory\n          ref: ${{ inputs.ref || 'v0' }}\n          token: ${{ secrets.FACTORY_GITHUB_TOKEN }}",
        string setup = "- uses: actions/setup-dotnet@v6\n        with:\n          global-json-file: global.json\n      ",
        string env = "FACTORY_GITHUB_TOKEN: ${{ secrets.FACTORY_GITHUB_TOKEN }}\n          ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}\n          EXAMPLE_API_KEY: ${{ secrets.EXAMPLE_API_KEY }}",
        string run = Run, string extra = "", string secondJob = "")
        => $"""
        name: factory
        on:
          {on}
        {permissions}
        jobs:
          factory:
            runs-on: {runsOn}
            if: {condition}
            steps:
              - uses: actions/checkout@v7
                with:
                  {checkout}
              {setup}{extra}- name: Run
                env:
                  {env}
                run: {run}
        {secondJob}
        """;

    private static string Ci(string on = "pull_request:\n  push:\n    branches: [main]", string permissions = "permissions:\n  contents: read",
        string integrationRuns = "- run: dotnet restore --locked-mode\n      - env:\n          EXAMPLE_API_KEY: ${{ secrets.EXAMPLE_API_KEY }}\n        run: dotnet run --no-restore --project tests/Integration",
        string integrationRunner = "ubuntu-latest", string needs = "needs: build", string buildSteps = "- run: dotnet restore --locked-mode\n      - run: dotnet build --no-restore\n      - run: dotnet format --verify-no-changes --no-restore\n      - run: dotnet test --no-restore",
        string prefix = "- uses: actions/checkout@v7\n      - uses: actions/setup-dotnet@v6\n        with:\n          global-json-file: global.json\n      ")
        => $"""
        name: ci
        on:
          {on}
        {permissions}
        jobs:
          build:
            runs-on: ubuntu-latest
            steps:
              {prefix}{buildSteps}
          integration:
            {needs}
            runs-on: {integrationRunner}
            steps:
              {prefix}{integrationRuns}
        """;

    private string Wf(string content) => Write("w.yml", content);

    [Fact]
    public void TheShippedShapePassesEveryCheck()
    {
        string f = Wf(Factory());
        WorkflowChecks.Trigger(f, "issues", "labeled");
        WorkflowChecks.Trigger(f, "workflow_dispatch");
        WorkflowChecks.RequiredInput(f, "workflow_dispatch", "issue", "number");
        WorkflowChecks.InputDefault(f, "workflow_dispatch", "ref", "v0");
        WorkflowChecks.NoOtherTrigger(f);
        WorkflowChecks.Permissions(f, "{}");
        WorkflowChecks.OneJob(f);
        WorkflowChecks.JobRuns(f, "ubuntu-latest", Condition);
        WorkflowChecks.FirstStepChecksOut(f, "sbiondolillo/dotnet-software-factory", "${{ inputs.ref || 'v0' }}", "${{ secrets.FACTORY_GITHUB_TOKEN }}");
        WorkflowChecks.SetupBeforeRun(f, Run);
        WorkflowChecks.LastStepRuns(f, Run);
        WorkflowChecks.RunStepEnvironment(f, Run, "FACTORY_GITHUB_TOKEN", "ANTHROPIC_API_KEY", "EXAMPLE_API_KEY");
        WorkflowChecks.NoOtherStepEnvironment(f, Run);
        string c = Wf(Ci());
        WorkflowChecks.TwoTriggers(c, "pull_request", "push", "main");
        WorkflowChecks.Permissions(c, "contents: read");
        WorkflowChecks.JobsOn(c, "ubuntu-latest", "build", "integration");
        WorkflowChecks.Needs(c, "integration", "build");
        WorkflowChecks.EachJobChecksOutAndSetsUp(c);
        WorkflowChecks.JobRunsInOrder(c, "build", ["dotnet restore --locked-mode", "dotnet build --no-restore", "dotnet format --verify-no-changes --no-restore", "dotnet test --no-restore"]);
        WorkflowChecks.JobRunsThen(c, "integration", "dotnet restore --locked-mode", "dotnet run --no-restore --project tests/Integration");
        WorkflowChecks.RunStepEnvironment(c, "dotnet run --no-restore --project tests/Integration", "EXAMPLE_API_KEY");
        WorkflowChecks.NoOtherStepEnvironment(c, "dotnet run --no-restore --project tests/Integration");
    }

    [Fact]
    public void AMissingFileFailsTheHoldsCheckAndAThirdFileFailsTheNoOtherCheck()
    {
        Write("factory.yml", "");
        Write("ci.yml", "");
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsFiles(_root, "factory.yml", "ci.yml", "review-feedback.yml"));
        Write("review-feedback.yml", "");
        WorkflowChecks.HoldsFiles(_root, "factory.yml", "ci.yml", "review-feedback.yml");
        WorkflowChecks.HoldsNoOther(_root, "factory.yml", "ci.yml", "review-feedback.yml");
        Write("x.yml", "");
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsNoOther(_root, "factory.yml", "ci.yml", "review-feedback.yml"));
    }

    [Fact]
    public void ALineCheckNeedsAWholeLine()
    {
        const string Section = "a\nuse `x` here\nb";
        WorkflowChecks.HoldsLine(Section, "b");
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsLine(Section, "use `x`"));
        WorkflowChecks.HoldsNoLine(Section, "use `x`");
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsNoLine(Section, "a"));
    }

    [Fact]
    public void TheAfterTheLabelCheckFailsOnAWrongOrderOrAMissingLabelLine()
    {
        WorkflowChecks.HoldsLineAfterLabel("about `x`\nnew", "new", "x");
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsLineAfterLabel("new\nabout `x`", "new", "x"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsLineAfterLabel("new", "new", "x"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsLineAfterLabel("about `x`", "new", "x"));
    }

    [Fact]
    public void AMissingTriggerOrAnotherTypeFailsTheTriggerChecks()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.Trigger(Wf(Factory(on: "issues:\n    types: [labeled]")), "workflow_dispatch"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.Trigger(Wf(Factory(on: "issues:\n    types: [opened]\n  " + Dispatch)), "issues", "labeled"));
    }

    [Fact]
    public void AThirdTriggerOrAMissingOneFailsTheNoOtherTriggerCheck()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.NoOtherTrigger(Wf(Factory(on: "issues:\n    types: [labeled]\n  " + Dispatch + "\n  push:"))));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.NoOtherTrigger(Wf(Factory(on: "issues:\n    types: [labeled]"))));
    }

    [Theory]
    [InlineData("workflow_dispatch:\n    inputs:\n      issue:\n        type: number")]
    [InlineData("workflow_dispatch:\n    inputs:\n      issue:\n        required: true\n        type: string")]
    [InlineData("workflow_dispatch:\n    inputs:\n      other:\n        required: true\n        type: number")]
    [InlineData("workflow_dispatch:")]
    public void AnotherIssueInputFailsTheRequiredInputCheck(string dispatch)
        => Assert.Throws<InvalidOperationException>(() => WorkflowChecks.RequiredInput(Wf(Factory(on: "issues:\n    types: [labeled]\n  " + dispatch)), "workflow_dispatch", "issue", "number"));

    [Theory]
    [InlineData("workflow_dispatch:\n    inputs:\n      ref:\n        default: main")]
    [InlineData("workflow_dispatch:\n    inputs:\n      ref:\n        required: false")]
    [InlineData("workflow_dispatch:\n    inputs:\n      other:\n        default: v0")]
    [InlineData("workflow_dispatch:")]
    public void AnotherRefInputFailsTheDefaultCheck(string dispatch)
        => Assert.Throws<InvalidOperationException>(() => WorkflowChecks.InputDefault(Wf(Factory(on: "issues:\n    types: [labeled]\n  " + dispatch)), "workflow_dispatch", "ref", "v0"));

    [Fact]
    public void AReadmeWithoutTheTrialRunSentenceOrCommandFailsTheCheck()
    {
        const string command = "gh workflow run factory -f issue=<number> -f ref=<ref>";
        WorkflowChecks.SaysTrialRun($"The repo's owner starts a trial run of the factory at another ref with the command `{command}`.", command);
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SaysTrialRun("The repo's owner starts a trial run of the factory at another ref.", command));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SaysTrialRun($"Run `{command}`.\n\nThe repo's owner starts a trial run of the factory at another ref.", command));
    }

    [Fact]
    public void WrongPermissionsFailTheCheck()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.Permissions(Wf(Factory(permissions: "permissions:\n  contents: read")), "{}"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.Permissions(Wf(Factory(permissions: "x: 1")), "{}"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.Permissions(Wf(Ci(permissions: "permissions:\n  contents: write")), "contents: read"));
    }

    [Fact]
    public void ASecondJobFailsTheOneJobCheck()
        => Assert.Throws<InvalidOperationException>(() => WorkflowChecks.OneJob(Wf(Factory(secondJob: "  other:\n    runs-on: ubuntu-latest"))));

    [Fact]
    public void ADifferentRunnerOrConditionFailsTheJobCheck()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobRuns(Wf(Factory(runsOn: "macos-latest")), "ubuntu-latest", Condition));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobRuns(Wf(Factory(condition: "true")), "ubuntu-latest", Condition));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobRuns(Wf(Factory(condition: "github.event.label.name == 'factory' && github.event.sender.login == github.repository_owner")), "ubuntu-latest", Condition));
    }

    [Theory]
    [InlineData("repository: other/repo\n          ref: ${{ inputs.ref || 'v0' }}\n          token: ${{ secrets.FACTORY_GITHUB_TOKEN }}")]
    [InlineData("repository: sbiondolillo/dotnet-software-factory\n          ref: v0\n          token: ${{ secrets.FACTORY_GITHUB_TOKEN }}")]
    [InlineData("repository: sbiondolillo/dotnet-software-factory\n          ref: ${{ inputs.ref || 'v0' }}\n          token: ${{ secrets.OTHER }}")]
    public void AnotherCheckoutFailsTheCheckoutCheck(string checkout)
        => Assert.Throws<InvalidOperationException>(() => WorkflowChecks.FirstStepChecksOut(Wf(Factory(checkout: checkout)), "sbiondolillo/dotnet-software-factory", "${{ inputs.ref || 'v0' }}", "${{ secrets.FACTORY_GITHUB_TOKEN }}"));

    [Fact]
    public void AFirstStepThatIsNotACheckoutFailsTheCheckoutCheck()
        => Assert.Throws<InvalidOperationException>(() => WorkflowChecks.FirstStepChecksOut(
            Wf("jobs:\n  a:\n    steps:\n      - run: echo\n"), "r", "v0", "t"));

    [Fact]
    public void AMissingMisplacedOrWrongSetupFailsTheSdkCheck()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SetupBeforeRun(Wf(Factory(setup: "")), Run));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SetupBeforeRun(Wf(Factory(setup: "", extra: "")).Replace("      - name: Run", "      - name: Run\n        run: " + Run + "\n      - uses: actions/setup-dotnet@v6\n        with:\n          global-json-file: global.json\n      - name: Again", StringComparison.Ordinal), Run));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SetupBeforeRun(Wf(Factory(setup: "- uses: actions/setup-dotnet@v6\n        with:\n          dotnet-version: 9\n      ")), Run));
    }

    [Fact]
    public void ALastStepThatDiffersFailsTheLastStepCheck()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.LastStepRuns(Wf(Factory(run: "echo")), Run));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.LastStepRuns(Wf(Factory(run: Run + " x")), Run));
    }

    [Fact]
    public void AMissingOrWrongSecretFailsTheEnvironmentCheck()
    {
        string[] names = ["FACTORY_GITHUB_TOKEN", "ANTHROPIC_API_KEY", "EXAMPLE_API_KEY"];
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.RunStepEnvironment(Wf(Factory(env: "FACTORY_GITHUB_TOKEN: ${{ secrets.FACTORY_GITHUB_TOKEN }}\n          ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}")), Run, names));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.RunStepEnvironment(Wf(Factory(env: "FACTORY_GITHUB_TOKEN: ${{ secrets.FACTORY_GITHUB_TOKEN }}\n          ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}\n          EXAMPLE_API_KEY: ${{ secrets.OTHER }}")), Run, names));
    }

    [Fact]
    public void AnotherStepWithAnEnvironmentFailsTheNoOtherCheck()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.NoOtherStepEnvironment(Wf(Factory(extra: "- env:\n          A: b\n        run: echo\n      ")), Run));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.NoOtherStepEnvironment(Wf(Ci(buildSteps: "- env:\n          A: b\n        run: dotnet test")), "dotnet run --no-restore --project tests/Integration"));
    }

    [Theory]
    [InlineData("pull_request:\n  push:\n    branches: [main]\n  schedule:")]
    [InlineData("pull_request:\n  push:\n    branches: [dev]")]
    [InlineData("push:\n    branches: [main]")]
    public void WrongTriggersFailTheCiTriggerCheck(string on)
        => Assert.Throws<InvalidOperationException>(() => WorkflowChecks.TwoTriggers(Wf(Ci(on: on)), "pull_request", "push", "main"));

    [Fact]
    public void MissingOrWrongJobsFailTheJobChecks()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobsOn(Wf(Factory()), "ubuntu-latest", "build", "integration"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobsOn(Wf(Ci(integrationRunner: "windows-latest")), "ubuntu-latest", "build", "integration"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.Needs(Wf(Ci(needs: "x: y")), "integration", "build"));
    }

    [Fact]
    public void ADotnetCommandBeforeSetupOrNoCheckoutFailsTheSetupCheck()
    {
        string before = "- run: dotnet restore\n      - uses: actions/checkout@v7\n      - uses: actions/setup-dotnet@v6\n        with:\n          global-json-file: global.json\n      ";
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.EachJobChecksOutAndSetsUp(Wf(Ci(prefix: before))));
        string noCheckout = "- uses: actions/setup-dotnet@v6\n        with:\n          global-json-file: global.json\n      ";
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.EachJobChecksOutAndSetsUp(Wf(Ci(prefix: noCheckout))));
        string late = "- uses: actions/checkout@v7\n      - run: dotnet restore\n      - uses: actions/setup-dotnet@v6\n        with:\n          global-json-file: global.json\n      ";
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.EachJobChecksOutAndSetsUp(Wf(Ci(prefix: late))));
    }

    [Fact]
    public void BuildCommandsOutOfOrderOrMissingFailTheCommandsCheck()
    {
        string[] expected = ["dotnet restore --locked-mode", "dotnet build --no-restore"];
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobRunsInOrder(Wf(Ci(buildSteps: "- run: dotnet build --no-restore\n      - run: dotnet restore --locked-mode")), "build", expected));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobRunsInOrder(Wf(Ci(buildSteps: "- run: dotnet restore --locked-mode")), "build", expected));
    }

    [Fact]
    public void IntegrationCommandsOutOfOrderOrMissingFailTheCheck()
    {
        string a = "dotnet restore --locked-mode";
        string b = "dotnet run --no-restore --project tests/Integration";
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobRunsThen(Wf(Ci(integrationRuns: $"- run: {b}\n      - run: {a}")), "integration", a, b));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobRunsThen(Wf(Ci(integrationRuns: $"- run: {b}")), "integration", a, b));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.JobRunsThen(Wf(Ci(integrationRuns: $"- run: {a}")), "integration", a, b));
    }

    [Fact]
    public void AnIntegrationStepWithoutTheSecretFailsTheEnvironmentCheck()
        => Assert.Throws<InvalidOperationException>(() => WorkflowChecks.RunStepEnvironment(
            Wf(Ci(integrationRuns: "- run: dotnet run --no-restore --project tests/Integration")), "dotnet run --no-restore --project tests/Integration", "EXAMPLE_API_KEY"));

    private string Readme(string text) => Write("README.md", text);

    private const string Good = $"""
        # T

        {Heading}

        ```
        dotnet new install <checkout>/templates/starter
        {Second}
        ```

        Push the copy to the branch `main` of a new GitHub repo.

        Add `FACTORY_GITHUB_TOKEN` and `ANTHROPIC_API_KEY` secrets.

        ## Next

        other
        """;

    [Fact]
    public void ASectionIsReadUpToTheNextLevelTwoHeading()
    {
        string readme = Readme(Good);
        Assert.DoesNotContain("other", WorkflowChecks.Section(readme, Heading), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.Section(readme, "## Missing"));
    }

    [Fact]
    public void CommandsInTheOtherOrderOrMissingFailTheCommandsCheck()
    {
        string section = WorkflowChecks.Section(Readme(Good), Heading);
        WorkflowChecks.HoldsCommands(section, "dotnet new install <checkout>/templates/starter", Second);
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsCommands(section, Second, "dotnet new install <checkout>/templates/starter"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.HoldsCommands(section, "dotnet new install <checkout>/templates/starter", "x"));
    }

    [Fact]
    public void APushSentenceBeforeTheCommandsDoesNotCount()
    {
        string good = WorkflowChecks.Section(Readme(Good), Heading);
        WorkflowChecks.PushesTo(good, Second, "main");
        string before = WorkflowChecks.Section(Readme($"{Heading}\n\nPush the copy to the branch `main` of a new GitHub repo.\n\n{Second}\n\nDone.\n"), Heading);
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.PushesTo(before, Second, "main"));
    }

    [Fact]
    public void ASaysAllTermMissingFromTheSectionFailsTheCheck()
    {
        string section = WorkflowChecks.Section(Readme(Good), Heading);
        WorkflowChecks.SaysAll(section, "`FACTORY_GITHUB_TOKEN`", "`ANTHROPIC_API_KEY`");
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SaysAll(section, "`FACTORY_GITHUB_TOKEN`", "Pull requests"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SaysAll(section, "other"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SaysAll(section, "Copilot"));
        Assert.Throws<InvalidOperationException>(() => WorkflowChecks.SaysAll(section, "`v0`"));
    }
}
