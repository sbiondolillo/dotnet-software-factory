# Dotnet Software Factory

The factory takes an issue of a .NET repo to a pull request. An agent writes the code, and the factory holds the gates, the review and the delivery.

## Recipes

**Recipe**:
A named, fixed sequence of phases, picked by a label of the issue. The four recipes are behavior, refactor, cleanup and reframe.
_Avoid_: run kind, mode

**Behavior**:
The recipe for a feature, a bug fix, or a new test for behavior the program already has. An issue with no recipe label is a behavior.
_Avoid_: feature run, default run

**Refactor**:
The recipe for a change to the structure of the code that adds no behavior. Every test stays as it is.
_Avoid_: restructure

**Cleanup**:
The recipe for a fix to a test, a config file or a document. The program stays as it is.
_Avoid_: chore, tidy

**Reframe**:
The recipe for a change to the code, the tests or the config that keeps the behavior, such as a format sweep or a framework upgrade. Every passing test keeps passing, and the public API of `src/` stays as it is.
_Avoid_: migration, upgrade

## Runs

**Run**:
One execution of a recipe. A factory run starts from an issue. A feedback run starts from the review threads of the pull request that a factory run opened.
_Avoid_: session, job

**Phase**:
One step of a recipe: the plan, the build or the review. A phase takes its inputs from the recipe and never reads the recipe.
_Avoid_: step, stage

**Redo**:
One more build after a gate or the review rejects the last build, with the faults of the rejection as Redo texts. A run has a budget of three Redos.
_Avoid_: retry, attempt

**Build loop**:
The part of a run that repeats the build within the Redo budget: the build, the lint, the gates in the order of the recipe, then the review. Both run kinds run the one build loop.
_Avoid_: redo loop, inner loop, attempt

**Decline**:
The end of a run before its first phase, on a fault of the issue's author, such as two recipe labels that exclude each other or a cleanup with no scope. The run posts the reason as a comment and builds nothing.
_Avoid_: refusal, rejection (a rejection is the review's verdict on a build)

**Settled**:
A pull request whose factory run passed. Every check is green, Copilot reviewed the head with no refusal, and no thread is unresolved.
_Avoid_: approved (an approval is the judge's verdict), reviewed

## Issue sections

**Scope**:
The paths that a cleanup or a reframe can change, one for each line under the heading `## Scope` of the issue.
_Avoid_: allow list, paths

**Check**:
The command in the first fenced block under the heading `## Check` of the issue, which fails before the build and passes after it. A cleanup and a reframe always have one, and a refactor can have one.
_Avoid_: acceptance test, verify step

## Gates

**Gate**:
A test of a build that the factory runs, outside the agent, before the review. A gate that fails gives a Redo.
_Avoid_: check (see Check), validation

**Pin**:
The fingerprint, taken before the build, of the files that the build must leave as they are. A refactor pins every test file and the build settings files, and every other recipe pins the feature files and the build settings files.
_Avoid_: snapshot, baseline

**Baseline**:
What a gate reads from the base commit before the first build, and holds from its construction: the tests that passed, for the lost-test check of the test gate, and the listing of the public API, for the API gate.
_Avoid_: snapshot, prepare

## Agents

**Agent**:
What a phase sends one request through and takes one answer from. A phase sees the agent alone, and never the adapter behind it.
_Avoid_: session, claude

**Output**:
A file that a phase asks the agent for by name, and that the answer carries as text. The agent keeps it fresh: an output in the answer is one that this prompt wrote.
_Avoid_: artifact, result (the result is the text of the answer)

## Repair

**Traveler**:
The block under `## Run` of a repair issue. The job that files the issue writes it. The jobs that build and merge the fix read it. It names the red run, the story, the target branch and its pull request, the head SHA and the session id.
