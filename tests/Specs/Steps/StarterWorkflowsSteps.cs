using Reqnroll;

namespace Specs.Steps;

[Binding]
public sealed class StarterWorkflowsSteps(StarterState state)
{
    private const string FactoryRun = "dotnet run --project src/App -c Release -- run --repo ${{ github.repository }} --issue ${{ inputs.issue || github.event.issue.number }} --pass-env EXAMPLE_API_KEY";
    private const string IntegrationRun = "dotnet run --no-restore --project tests/Integration";

    private string Workflow(string name) => Path.Combine(state.Copy, ".github", "workflows", name);

    private static string Readme() => Path.Combine(StarterChecks.RepoRoot(), "README.md");

    private static string Section(string heading) => WorkflowChecks.Section(Readme(), heading);

    [Then("the copy's {string} holds the files {string}, {string} and {string}")]
    public void ThenTheCopyHoldsTheFiles(string directory, string first, string second, string third)
        => WorkflowChecks.HoldsFiles(Path.Combine(state.Copy, directory), first, second, third);

    [Then("the copy's {string} holds no other file")]
    public void ThenTheCopyHoldsNoOtherFileIn(string directory)
        => WorkflowChecks.HoldsNoOther(Path.Combine(state.Copy, directory), "factory.yml", "ci.yml", "review-feedback.yml");

    [Then("the workflow {string} has no other trigger")]
    public void ThenNoOtherTrigger(string file) => WorkflowChecks.NoOtherTrigger(Workflow(file));

    [Then("the workflow {string} has the permissions {string}")]
    public void ThenPermissions(string file, string permissions) => WorkflowChecks.Permissions(Workflow(file), permissions);

    [Then("the workflow {string} has one job")]
    public void ThenOneJob(string file) => WorkflowChecks.OneJob(Workflow(file));

    [Then("that job runs on {string} with the condition {string}")]
    public void ThenThatJobRuns(string runner, string condition) => WorkflowChecks.JobRuns(Workflow("factory.yml"), runner, condition);

    [Then("the first step of the job in {string} checks out the repo {string} at the ref {string} with the token {string}")]
    public void ThenFirstStepChecksOut(string file, string repo, string reference, string token)
        => WorkflowChecks.FirstStepChecksOut(Workflow(file), repo, reference, token);

    [Then("a step of the job in {string} sets up the .NET SDK that the global.json of the checkout names, before the step with the run command {string}")]
    public void ThenASetupStep(string file, string run) => WorkflowChecks.SetupBeforeRun(Workflow(file), run);

    [Then("the last step of the job in {string} runs {string}")]
    public void ThenLastStepRuns(string file, string run) => WorkflowChecks.LastStepRuns(Workflow(file), run);

    [Then("the step with the run command in {string} has {string}, {string} and {string} in its environment, each from the secret of the same name")]
    public void ThenRunStepEnvironment(string file, string first, string second, string third)
        => WorkflowChecks.RunStepEnvironment(Workflow(file), FactoryRun, first, second, third);

    [Then("no other step in {string} has an environment")]
    public void ThenNoOtherStepEnvironment(string file)
        => WorkflowChecks.NoOtherStepEnvironment(Workflow(file), file == "ci.yml" ? IntegrationRun : FactoryRun);

    [Then("the workflow {string} has two triggers, the event {string} and the event {string} on the branch {string}")]
    public void ThenTwoTriggers(string file, string first, string second, string branch) => WorkflowChecks.TwoTriggers(Workflow(file), first, second, branch);

    [Then("the workflow {string} has the jobs {string} and {string}, each on {string}")]
    public void ThenJobs(string file, string first, string second, string runner) => WorkflowChecks.JobsOn(Workflow(file), runner, first, second);

    [Then("the job {string} needs the job {string}")]
    public void ThenJobNeeds(string job, string needed) => WorkflowChecks.Needs(Workflow("ci.yml"), job, needed);

    [Then("each job of {string} checks out the repo and then sets up the .NET SDK that global.json names, before its first dotnet command")]
    public void ThenEachJobSetsUp(string file) => WorkflowChecks.EachJobChecksOutAndSetsUp(Workflow(file));

    [Then("the job {string} of {string} runs these commands in order")]
    public void ThenJobRunsInOrder(string job, string file, Table table)
        => WorkflowChecks.JobRunsInOrder(Workflow(file), job, table.Rows.Select(row => row["command"]));

    [Then("the job {string} of {string} runs {string} and then {string}")]
    public void ThenJobRunsThen(string job, string file, string first, string second) => WorkflowChecks.JobRunsThen(Workflow(file), job, first, second);

    [Then("the step with the integration command in {string} has {string} in its environment, from the secret of the same name")]
    public void ThenIntegrationStepEnvironment(string file, string name) => WorkflowChecks.RunStepEnvironment(Workflow(file), IntegrationRun, name);

    [Then("the README section {string} holds the commands {string} and {string} in that order")]
    public static void ThenReadmeHoldsCommands(string heading, string first, string second) => WorkflowChecks.HoldsCommands(Section(heading), first, second);

    [Then("the README section {string} says, after the two commands, that the person pushes the copy to the branch {string} of a new GitHub repo")]
    public static void ThenReadmePushes(string heading, string branch)
        => WorkflowChecks.PushesTo(Section(heading), "dotnet new factory-starter -o <directory>", branch);

    [Then("the README section {string} names the Actions secrets {string} and {string}")]
    public static void ThenReadmeNamesSecrets(string heading, string first, string second)
        => WorkflowChecks.SaysAll(Section(heading), $"`{first}`", $"`{second}`", "secrets");

    [Then("it says that {string} is a fine-grained token with read and write access to {string}, {string} and {string} on the new repo, and read access to {string} on {string}")]
    public static void ThenReadmeSaysToken(string token, string a, string b, string c, string d, string repo)
        => WorkflowChecks.SaysAll(Section("## Start a new repo"), $"`{token}`", "fine-grained token", "read and write access to " + a + ", " + b + " and " + c + " on the new repo", "read access to " + d + " on `" + repo + "`");

    [Then("the README section {string} says that the branch {string} is protected, with the checks {string} and {string} required")]
    public static void ThenReadmeProtects(string heading, string branch, string first, string second)
        => WorkflowChecks.SaysAll(Section(heading), "Protect the branch", $"`{branch}`", $"`{first}`", $"`{second}`", "required");

    [Then("the README section {string} says that Copilot code review is requested on every pull request")]
    public static void ThenReadmeCopilot(string heading) => WorkflowChecks.SaysAll(Section(heading), "requests a Copilot code review of each pull request it watches");

    [Then("the README section {string} says that the repo's owner starts a run with the label {string} on an issue, and that {string} names the tag {string} of the factory")]
    public static void ThenReadmeRunStart(string heading, string label, string file, string tag)
        => WorkflowChecks.SaysAll(Section(heading), "owner starts a run with the label `" + label + "` on an issue", $"`{file}` names the tag `{tag}` of the factory");
}
