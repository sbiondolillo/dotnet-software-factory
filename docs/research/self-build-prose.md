# What the factory reads from the workpiece's prose, and whether DSF's own serves

Research for issue #275, read off `main` at `ecd0afb` on 2026-10-13. Each line below names the class, the file and the line that decides it.

## Summary

The factory loads no prose file from the workpiece. Each prompt holds the issue title and body, a fixed text, and for the review the diff. The agent reads every other file itself, with its tools, inside the sandbox. The sandbox copies nothing: it bind-mounts the whole clone at `/work` and runs `claude` there. So `CLAUDE.md` and `.claude/` reach the agent through Claude Code's own instruction loading, and `AGENTS.md` reaches it through the `@AGENTS.md` import. DSF lacks the starter's `write-issue` skill, `docs/write-an-issue.md`, the issue templates and the two workflows, and the factory never looks for any of them. Two texts of DSF's own prose contradict the factory's prompts. `CLAUDE.md` and the `story` skill tell the agent to claim the issue with `gh`, work in a worktree, commit and open the pull request. `AGENTS.md` says a build never edits a feature file. One blocker is not prose. The test gate runs DSF's integration suite inside a sandbox that has no `docker` and no `FACTORY_GITHUB_TOKEN`. So a self-build fails before the review phase.

## Which workpiece files the build prompt, the plan prompt and the review prompt load, and from where

**Verdict: none.** No prompt reads a file of the workpiece. The prompts name paths, and the agent opens them itself in the sandbox.

- The issue comes from GitHub, not from the clone: `FactoryRun.RunCoreAsync` reads it with `GitHubClient.ReadIssueAsync` (`src/Core/FactoryRun.cs`, line 62). Every prompt gets `issue.Title` and `issue.Body` as text.
- The plan prompt is `PlanPhase.BuildPrompt` (`src/Core/PlanPhase.cs`, lines 54 to 74). It names `specs/features/` (line 15) and `.factory/plan.md` (line 16). With scenarios from the issue, it names the feature files the issue wrote (lines 58 and 59). Those names come from `IssueScenarios.Apply`, which parses the issue body (`src/Core/FactoryRun.cs`, line 148).
- The build prompt is `BuildPhase.Compose` (`src/Core/BuildPhase.cs`, lines 89 to 147). It names `.factory/plan.md`, `specs/features/`, `tests/Specs/Steps/` and `tests/Specs/Unit/` (lines 137 to 140), and the settings file names from `Pin.SettingsFileNames` (line 142, defined in `src/Core/Pin.cs`, lines 14 to 26). Redo texts come from the gates and the review, not from a file (lines 149 to 158).
- The review prompt is `ReviewPhase.BuildPrompt` (`src/Core/ReviewPhase.cs`, lines 101 to 127). It is the only prompt that inlines content of the work tree. That content is the diff against the base commit, from `Git.ReadDiffAsync` (line 29). The diff is cut to the prompt limit (lines 129 to 151). It names `specs/features/` and `.factory/plan.md` (lines 120 and 121).
- The feedback prompt (`src/Core/FeedbackRun.cs`, lines 190 to 211) holds the review threads and the report path, and no file.
- `grep -rn 'AGENTS.md\|CLAUDE.md\|\.claude\|docs/' src/Core sandbox` finds nothing. The factory has no code that names a prose file.

The one workpiece file the factory itself reads for a run is not prose: `SandboxImage.EnsureAsync` reads `global.json` (`src/Core/SandboxImage.cs`, lines 20 to 24). It reads it from `rootDirectory`, which is the factory's own checkout, the current directory of the console program (`src/App/Program.cs`, line 23). For a self-build the two checkouts are the same repo.

## Which files the sandbox copies in or mounts, and whether `CLAUDE.md` and `.claude/` reach the agent

**Verdict: the sandbox copies nothing and mounts everything. `CLAUDE.md` and `.claude/` reach the agent.**

- The clone is a full `git clone` (`Git.CloneAsync`, `src/Core/Git.cs`, line 12). Every tracked file is in the work tree. An untracked file such as `CLAUDE.local.md` or `.claude/settings.local.json` is not.
- `Sandbox.StartAsync` bind-mounts the work tree at `/work` (`src/Core/Sandbox.cs`, line 30). It mounts `.git` read-only and `.git/worktrees` writable over it (lines 36 to 38). The working directory is `/work` (line 52). `HOME` is an empty tmpfs at `/tmp/sandbox-home` (lines 53 and 54), so no user-level `~/.claude` exists.
- `ClaudeCodeAgent.RunAsync` runs `claude -p <prompt> --model --max-turns --output-format json --dangerously-skip-permissions` in that container (`src/Core/ClaudeCodeAgent.cs`, lines 36 to 46), with `docker exec` and no `--workdir`, so in `/work`.
- The image installs the latest `@anthropic-ai/claude-code` with no pin (`sandbox/Dockerfile`, line 14). The version is the one on npm when the image was built.

What Claude Code loads from `/work` is its own behaviour, not the factory's. The Claude Code docs (`code.claude.com/docs/en/memory.md`, `headless.md`, `skills.md`, read 2026-10-13) state:

- `-p` mode loads the same context as an interactive session unless `--bare` is passed. The factory passes no `--bare`.
- `CLAUDE.md`, `.claude/CLAUDE.md` and `CLAUDE.local.md` in the working directory load at launch. An `@path` line imports that file.
- `AGENTS.md` loads on its own only when no `CLAUDE.md` exists in the working directory or above it, from Claude Code 2.1.277.
- Project skills under `.claude/skills/<name>/SKILL.md` load, and the model can invoke one when its `description` matches the task. `disable-model-invocation: true` makes a skill manual-only.
- `.claude/settings.json` in the project loads, and its hooks run. The docs do not say that `--dangerously-skip-permissions` disables hooks.
- `.github/ISSUE_TEMPLATE/*.md` and `docs/*.md` are never loaded on their own.

So for the starter, which ships `AGENTS.md` and no `CLAUDE.md`, the rules reach the agent only through the `AGENTS.md` fallback. For DSF, `CLAUDE.md` opens with `@AGENTS.md` (`CLAUDE.md`, line 1), so both files reach the agent, and so does the `story` skill.

## Which of those files DSF lacks, and what the factory does when one is missing

**Verdict: DSF lacks five starter files, and the factory does nothing about any of them, because it never reads them.**

| Starter file | DSF | What the factory does |
|---|---|---|
| `AGENTS.md` | present, with different text | nothing. Reaches the agent through `@AGENTS.md` |
| `.claude/skills/write-issue/` | absent. DSF has `.claude/skills/story/` | nothing. It shapes a body on the drafting side only |
| `docs/write-an-issue.md` | absent | nothing. Same |
| `.github/ISSUE_TEMPLATE/*.md` | absent | nothing. GitHub reads it when a person opens an issue |
| `.github/workflows/factory.yml` | absent | nothing. It is the trigger, not an input: the run starts from the console program |
| `.github/workflows/review-feedback.yml` | absent | nothing. Same, for the `feedback` verb |

The factory checks exactly four layout paths and no prose: `Solution.slnx`, `specs/features/`, `tests/Specs/` and `tests/Integration/` (`src/Core/FactoryRun.cs`, lines 41 and 42). A missing one gets the comment "The clone lacks these paths of the layout:" and no run (lines 134 to 142). DSF has all four.

The factory reads the issue body with `IssueScenarios.Holds` and `IssueScenarios.Apply` (`src/Core/FactoryRun.cs`, lines 146 to 148). A body with `## Scenarios` blocks whose first line is `# specs/features/<name>.feature` parses. The `story` skill writes that form (`.claude/skills/story/SKILL.md`, line 37), so an issue the skill drafts is a body the factory runs.

The settings files are optional. `Pin.Scan` hashes each one it finds (`src/Core/Pin.cs`, lines 67 to 80), and a file that is absent is not in the pin.

One absence that is not prose blocks a self-build. `TestGate.RunAsync` runs `dotnet run --no-restore --project tests/Integration` in the sandbox on every pass of the build suite (`src/Core/TestGate.cs`, lines 121 to 127). DSF's integration suite asserts `FACTORY_GITHUB_TOKEN` (`tests/Integration/GitHubClientIntegrationTests.cs`, line 17, and eight more files) and runs `docker` (`tests/Integration/SandboxIntegrationTests.cs`, line 27). The sandbox holds `ANTHROPIC_API_KEY` only (`src/Core/Sandbox.cs`, line 55), plus the `--pass-env` names for that one command (`src/Core/TestGate.cs`, line 123). The image has no `docker` binary (`sandbox/Dockerfile`, lines 5 to 7). So the test gate fails on every self-build, the three Redos burn on the same fault, and no review phase runs.

## Whether anything in DSF's `AGENTS.md` or `CLAUDE.md` contradicts what the factory tells the agent

**Verdict: yes, in three places. Two are in `CLAUDE.md` and the `story` skill, and one is in `AGENTS.md`.**

1. `CLAUDE.md`, line 3: "A change to this repo starts with the `story` skill ... builds it on a branch once the owner labels it `ready`, and opens the pull request." The build prompt says "Build the work of the issue below ... Run dotnet build and dotnet test, and work until both pass" (`src/Core/BuildPhase.cs`, lines 134 to 143). The factory commits with `git` on the host (`src/Core/LintGate.cs`, line 28) and opens the pull request itself (`src/Core/Deliver.cs`). An agent that follows `CLAUDE.md` does the factory's work by other means.

2. `.claude/skills/story/SKILL.md`, line 3, the `description`: "Use when a change to this repo is requested, or when an issue with the label ready is to be built." The build prompt is a request to build an issue. So the model can invoke the skill on its own. The skill then tells the agent to do five things.
   - Run `gh issue edit` (line 87). The image does not hold `gh`: `sandbox/Dockerfile` installs `git curl ca-certificates libicu72` (line 6).
   - Run `git worktree add .claude/worktrees/<n>-<slug>` (line 99). The sandbox permits it (`src/Core/Sandbox.cs`, line 38). The work then sits where `dotnet test` in `/work` does not build it.
   - Run `git commit` (line 117). It fails on the read-only `.git` (`src/Core/Sandbox.cs`, line 38, and `AGENTS.md`, lines 117 to 121).
   - Run three reviewers "on Opus" as subagents (line 126).
   - Run `gh pr create` (line 150).

   Each step spends turns of the budget and changes nothing the gates read.

3. `AGENTS.md`, line 11: `specs/features/` holds "the pinned `.feature` files" and "a build reads them and never edits them." The plan prompt says "Write Gherkin scenarios for the behaviour in feature files under specs/features/" (`src/Core/PlanPhase.cs`, lines 71 and 72). It also says "Remove each feature file whose behaviour the issue removes" (line 73). The plan gate fails when no feature file is new or changed (lines 132 to 134). An agent that honours "never edits them" fails the plan gate. The starter's row says "the `.feature` files" with no such rule (`templates/starter/AGENTS.md`, line 9).

The rest of `AGENTS.md` agrees with the factory. "What a scenario tests" (line 19) is the starter's rule with DSF's examples. The `## Claude Code` section (lines 124 to 145) describes the command the factory runs, as `src/Core/ClaudeCodeAgent.cs` does. Line 7 calls `docs/agents/setup-sheet.toml` the authority for two directories. That file belongs to the homelab factory. No DSF code reads it, and its values agree with the hard-coded paths.

## Files DSF must add, change or leave

| File | Action | Reason |
|---|---|---|
| `CLAUDE.md` | change | Keep `@AGENTS.md`. Reword line 3 so that it governs an interactive session and not a factory run, or move it into the skill |
| `.claude/skills/story/SKILL.md` | change | Add `disable-model-invocation: true` to the front matter, so the build prompt cannot invoke it. Leave the body |
| `.claude/skills/story/JUDGES.md` | leave | Reached only through the skill |
| `AGENTS.md` | change | Line 11: replace "the pinned `.feature` files ... a build reads them and never edits them" with the starter's wording. Leave every other section |
| `tests/Integration/` | change, or change the factory | The test gate runs it in the sandbox with no `docker` and no token, so every self-build fails there. Either each test skips without its service, which `AGENTS.md` line 13 forbids today, or the factory gets a way to leave the suite out. The second is a factory issue |
| `.github/workflows/factory.yml` | add, adapted | The starter's checks out `sbiondolillo/dotnet-software-factory` at `v1` and runs it against `github.repository`. For DSF the two are one repo, so the ref decides which factory builds which main. Without it, the operator runs `dotnet run --project src/App -- run --repo sbiondolillo/dotnet-software-factory --issue <n>` from the checkout |
| `.github/workflows/review-feedback.yml` | add, optional | Only if the `feedback` verb is wanted on DSF's own pull requests |
| `.claude/skills/write-issue/` | leave absent | The factory never reads it. The `story` skill writes a body that `IssueScenarios.Apply` parses |
| `docs/write-an-issue.md` | leave absent | The factory never reads it. Add it only if the recipe labels `refactor`, `cleanup` and `reframe` are to be used on DSF, as the rules for those bodies |
| `.github/ISSUE_TEMPLATE/*.md` | leave absent | GitHub reads them, the factory does not. The `story` skill drafts the body |
| `Solution.slnx`, `specs/features/`, `tests/Specs/`, `tests/Integration/` | leave | The four layout paths pass (`src/Core/FactoryRun.cs`, lines 134 to 142) |
| `.editorconfig`, `Directory.Build.props`, `global.json`, `tests/Specs/reqnroll.json` | leave | Pinned by `Pin.Scan`. Present and unchanged by a build |
| `sandbox/Dockerfile` | leave | No `gh` and no `docker` is correct once the skill cannot fire and the integration run is settled |
| `docs/agents/setup-sheet.toml` | leave | No DSF code reads it |
