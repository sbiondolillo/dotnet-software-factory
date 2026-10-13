# DSF v2: the designs settled before the map, and the work no map holds

Moved from `sam/homelab` `docs/TODO.md` on 2026-10-13, when the map [DSF v2: the factory builds its own repo from a GitHub-hosted runner](https://github.com/sbiondolillo/dotnet-software-factory/issues/273) was charted. The map's Not yet specified and Out of scope sections point here. A link of the form `sam/homelab` `docs/...` names a file in that repo, on the homelab's own Gitea.

A section here is one of three things. A capability or a fault is fog: it becomes a ticket or a map when the owner picks it. A design input is out of scope for the map: it is a settled or half-settled design that waits for a later effort. DSF work with no map is a story filed with `/change` when the owner picks it, or a direct change, as `.claude/skills/change/SKILL.md` routes it.

## The capabilities the homelab factory is the template for

Since 2026-10-09 Claude builds DSF directly, on a branch, with a frontier model (`sam/homelab` `docs/decisions/od-33.md`). Since #280, on 2026-10-13, the factory builds a DSF story by default, and a direct session builds the rest, as `.claude/skills/change/SKILL.md` states. The homelab factory is the template, and these are the capabilities to reproduce on GitHub, each as a later map:

- **An OpenTelemetry pipeline into Grafana.** One trace per run, one span per phase and per tool call, and a
  waterfall that drills into one run. The lab's reference is OD-26 (ClickHouse on `nas-01`) and OD-27 (the
  gateway's spend ledger). The collector and the store are swaps: a local part of DSF runs anywhere.
- **A harness evaluation.** A battery of issues with a known pass, run against a factory commit, scored to a
  table. The lab's reference is `homelab-mcp` `eval/`.
- **A self-improvement loop.** The feedback loop below, with DSF as its first real workpiece.

## The trial faults

- **The DSF faults that the `factory#217` trial found** (`sam/homelab` `docs/log/2026-10-09-factory-217-starter-trial.md`).
  Each is a DSF story, filed from the DSF workspace with `/change`. All are seen at DSF `352f531`:
  - The factory reads Copilot's quota refusal as a review with 0 threads, and the gate passes
    (`forecast-sample` `#6` and `#8`). A refusal is not a review.
  - The pull request body prints the cost as a raw double: `$0.21640510000000003`.
  - The starter's `src/App/App.csproj` sets `AssemblyName` to `dotnet-software-factory`.
  - Reqnroll writes `tests/Specs/Reqnroll/userid` into the project when `HOME` has no `.config`. This is a
    lead: `starter-trial` caught it in its first commit.
  - `TestGate` runs `dotnet test --logger trx`, so a target on xunit 4 and Microsoft.Testing.Platform fails.
  - The review passed `forecast-sample` `#4`, which edited `tests/Integration/GeocodingTests.cs`, against
    the issue's "Every existing test passes unchanged".
  - The starter `AGENTS.md` style item lists too few by-hand cases: `value == null` and `x == 0` are missing.
  - The rules page `docs/write-an-issue.md` gives the starter's `greeting.feature` as its example. A repo
    that replaces the greeter, such as `forecast-sample`, then holds a page about a file it does not have.
  - The headless `write-issue` session filed its issue with no judge round after its last edit.
  - A rule for `write-issue`: a count of findings names the command that lists them. A count of `CA1849`
    was wrong by one in a draft of `forecast-sample` `#5`.


## Design inputs, out of scope for the map

- **A model roster.** v1 names Claude Code as its one agent. Models from more than one provider are a v2
  question (operator, 2026-10-04).
- **The skill `write-issue` in a shared directory.** v1 puts it in the starter's `.claude/skills/` only, which
  Claude Code and GitHub Copilot read. `.agents/skills/` is the directory that Copilot, Cursor, Codex CLI and
  Gemini CLI read, and Claude Code does not (docs read 2026-10-04). It goes with the model roster.
- **A design input: Copilot code review as a standards reviewer.** Copilot code review loads a repo skill from
  `.github/skills/<name>/SKILL.md`. The issue-against-diff check already lives in `src/Core/ReviewPhase.cs`,
  which puts the issue and the diff in the prompt in code. A Copilot skill would check the C# rules of
  `.editorconfig` and `layout.md` instead. Its comments would be one input to `src/Core/ReviewThreads.cs`.
  It needs a Copilot Pro plan or higher, so it stays out of `templates/starter` (operator, 2026-10-03).
- **A design input: the package as a public image on GHCR.** A starter repo's `factory.yml` checks out DSF's
  source today, runs `setup-dotnet`, and builds it with `dotnet run` on every run. A public image would skip that
  build, pin the factory by digest where the tag `v1` moves, and leave a starter repo less of the factory. The
  factory builds its sandbox image from its own `sandbox/` at run time (`src/Core/SandboxImage.cs`), so the image
  carries that folder or a second image ships beside it. Not a fix for token scope: a public repo already needs no
  token to check out (`sam/homelab#161`), and a private GHCR image needs a classic token to pull, as far as the
  2026-10-07 session knew. Check GitHub's Packages docs before the design (operator, 2026-10-07).

- **A design input: a whole-file pin of a prose file.** Every starter file is pinned whole in a feature
  file, as a docstring that equals the file: `starter-workflows.feature` holds `factory.yml`,
  `starter-agents.feature` holds `AGENTS.md`, and `#206` to `#208` added a 96-line page and two skill files
  the same way. The pin is what lands a text byte for byte through an agent, and the three PRs of
  `factory#209` passed every mutation because of it. The cost: the text lives three times, in the issue, the
  feature and the starter, and each later edit restates the whole file twice. `#206` paid that to add three
  lines to `AGENTS.md`, and `#208` paid five runs where its sibling's pin and a body fault met. The suite
  already proves the copy equals the starter file by file, so the whole-text scenario guards the starter's
  content alone. A lighter pin, the front matter and the headings, lets the build paraphrase the body, and
  for a skill the words are the product. Decide once for every pinned prose file, with the README ticket
  `factory#210` or after it (operator question, 2026-10-07).


- **The issue review, settled 2026-10-07.** A workflow in the starter judges an issue on a label, edits its body,
  and applies `ready`. It waits for `sam/homelab-factory#209`, which ports the rules and the three judge angles
  as `REVIEW.md` in `write-issue`. The workflow reads that file at the checkout's commit, so the judge and the
  code it judges against are one version. Three labels, one meaning each:

  | Label | Who applies it | Meaning |
  |---|---|---|
  | `review` | the author, person or agent | judge this body and fix it |
  | `ready` | the review agent, on three accepts | the body passed the judge |
  | `factory` | the operator | build it now |

  The job has the trigger and the owner check of `factory.yml`. It runs the three angles, edits the body in
  place, posts one comment with the three verdicts and the diff of its edits, and removes `review`. On three
  accepts it adds `ready`. On a reject it adds nothing. A label that `GITHUB_TOKEN` applies comes from
  `github-actions[bot]`, which the owner check of `factory.yml` rejects, so the agent cannot start a build. The
  agent applies `factory` only once its verdicts hold, with a PAT of the owner, the same step as the feedback
  loop's triage. Two of the angles read `main` and run the suite, so the job runs on a GitHub-hosted runner with
  an API key, and the fleet stays out of it. `ready` stays the verdict: a request label that the agent removes
  on a reject would mean "claimed ready". A trigger on `opened` rewrites a draft before its author asks.

  **`REVIEW.md` has four angles since DSF `#242` (`4fccf59`).** The fourth, `Holes`, judges a body with
  `## Check`. It runs each likely hole in a copy of `main` under `docker`, by `PROOF.md`, and reads the proof
  comment that the skill's step 4 posts. The design states whether the job runs `Holes` and the proof itself,
  or reads the author's proof comment. "Three angles" and "three accepts" above predate it.

  **A reject that cites a rule is the draft's fault. A reject that says `no rule` is the skill's.** Only the
  second files an issue: a `cleanup` on DSF with no `ready`, naming the rejected issue, the fault, and the rule
  it proposes. The operator reads it as any draft, and the fix is itself a factory build. An issue on every
  reject would duplicate the comment with no change to make. The operator's read of the reject comments is the
  backstop for the gaps the agent misses in its own rulebook.

  **A reviewer's `no rule` mark is not yet reliable.** In the first stranger session of `factory#209`, two
  reviewers marked a finding `no rule`, and a rule of the page covered each one. This design would have filed
  two false DSF drafts. Before this loop files on a `no rule` mark, each reviewer checks every rule of the page
  first: `sam/homelab` `docs/log/2026-10-07-factory-209-stranger-session.md`.

- **The feedback loop, settled 2026-10-06.** It is not built, and the decision to build it waits for the first
  workpiece that is more than a sample repo. The sample repos smooth the factory's rough edges by hand. The loop
  has five parts, and DSF holds the last one today.
  1. **The record.** The build and review prompts in `src/Core/BuildPhase.cs` and `src/Core/ReviewPhase.cs`
     ask for a Report, the facts about a tool or library the agent got wrong, each with the command that
     proved it. The run writes the Report, the Redo texts, the gate outputs and the review threads as one JSON
     file under `$RUNNER_TEMP`, outside the work tree the model writes.
  2. **The channel.** A second step in the starter's `factory.yml`, with `if: always()`, posts the record as
     an issue on DSF with the label `run-report`. The step holds its own secret with Issues write on DSF and
     no access to the workpiece. The factory step keeps `FACTORY_GITHUB_TOKEN`, and the sandbox gives the
     model `ANTHROPIC_API_KEY` only (`src/Core/Sandbox.cs`), so each party holds one token. *Amended
     2026-10-10: true since DSF `#263` (`947713e`). Before it, a git hook or config line that the agent wrote
     in the work tree ran on the runner with the job's secrets (`sam/homelab` `docs/log/2026-10-10-factory-210-readme.md`)).*
  3. **Triage.** A DSF workflow runs a triage agent on each new `run-report` issue. Its verdicts are act, hold
     and discard. Hold is the store: the open `run-report` issues, read together, so one run's variance is
     held until a second run pays for the same trap. Act proves the claimed command in a sandbox first, then
     files one draft issue, a `cleanup` with `## Scope` and `## Check` or a `behavior` with scenarios, and
     closes each report it drains with a comment. The operator applies the label `factory`. When the drafts
     are good enough, the agent applies the label itself. Its token is then a fine-grained PAT of the owner,
     since DSF's `factory.yml` checks that the sender is the owner. The merge stays the operator's.
  4. **The actuator.** A trap fix is a prompt line, and the prompts are C# constants, so the first module is a
     `refactor` that moves the prompt text to data. A fix is then a `cleanup`. A fix to the starter's
     `AGENTS.md` reaches new repos only.
  5. **The gate and the release.** The starter's trial run at a ref, `gh workflow run factory -f issue=N -f
     ref=<branch>`, reruns the originating workpiece issue on the fix branch. The release moves the tag
     after the rerun passes.

  The input rate is the operator's own `factory` labels on workpieces, and one report drains into at most
  one draft issue, so the loop needs no budget of its own. The build order is 4, 1, 2, 3, 5: the first two
  stand without the channel.


## DSF work with no map

- **The stories after DSF `#236`.** `#236` makes the factory pin the feature files of an issue's `## Scenarios`
  as written, and `PlanPhase` writes only the plan. Each item below is a DSF story, filed with DSF's `change`
  skill when `#236` merges:
  - The plan names one integration test for each `Given` that holds a service's answer, and the review
    blocks a build that lacks one. On `main` only `AGENTS.md` asks for these tests, and no gate checks them.
- **IDE0005 at `warning`.** Added on 2026-10-07, from the strict proof of `homelab-factory#212`. IDE0005 (an
  unused `using`) runs in the build only when `GenerateDocumentationFile` is `true`, with CS1591 in `NoWarn`.
  Both go in the root `Directory.Build.props`. DSF `main` `5ce5e6f` has 11 findings, all in tests. The operator
  chose to wait (2026-10-07). Since OD-33, Claude builds DSF directly, so no grader blocks this change. Done
  when DSF's root `Directory.Build.props` sets `GenerateDocumentationFile` and DSF builds with IDE0005 at
  `warning`.
