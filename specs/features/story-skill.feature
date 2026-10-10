Feature: Story skill

  Scenario: The body of a story is the handoff, and holds no file whole and no block of source code
    Then the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "The body of the issue is the **handoff**."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "The body holds no file whole and no block of source code, whole or in part: not a file, a member or a line."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "| `## Context` | Its first line is `` `main` at `<full SHA>` ``."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "Done when the issue is filed with no label, and the handover gives its URL. Then stop."
    And the file ".claude/skills/story/SKILL.md" of this repo holds no line that holds "gh issue comment"
    And the file ".claude/skills/story/SKILL.md" of this repo holds no line that holds "## Design"

  Scenario: A prototype of a story goes to the build as the branch design/<n>
    Then the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "`## Context` reports what it showed, in a table: each mutation, and the scenario that failed it or the word `pass`. The code of the prototype stays out of the body."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "git -C <prototype worktree> push origin HEAD:refs/heads/design/<n>"
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "git -C <prototype worktree> push --force origin HEAD:refs/heads/design/<n>"
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "`<start>` is `origin/main`, or `origin/design/<n>` when `## Context` names a prototype"
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "From a prototype, when `main` moved since the SHA that `## Context` names, run `git rebase origin/main` first."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "Each name under `## Removed scenarios` is removed from its feature file."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "`git push origin --delete design/<n>` removes a prototype branch"

  Scenario: The judges of the story skill fault a file held whole or a block of source code in a draft
    Then the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "A file held whole in the body, or a block of source code, whole or in part, is a fault: the body names the path, the member and the behaviour, and the prototype holds the text."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "report the command and its output, not the code of the proof"
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "Read each text a scenario pins as `git diff <base>...<SHA> -- <path>`, against `**What changes.**`."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds no line that holds "cmp <saved> <path>"

  Scenario: A prototype is built for a pinned text or a named hole, and for nothing else
    Then the file ".claude/skills/story/SKILL.md" of this repo holds the line "A **prototype** is built for two reasons only: a change to a text that a scenario pins, so the owner reads a diff, or a hole that a judge named with its scenario, its break and its command. This session builds it in a scratch worktree of `main`, `git worktree add --detach <scratch dir> <main SHA>`. Any other draft has no prototype. Code is not such a text: a scenario pins what code does. `## Context` reports what it showed, in a table: each mutation, and the scenario that failed it or the word `pass`. The code of the prototype stays out of the body. The rows are the prototype as it is, each changed file of `main` put back on its own, and each narrower mutation. A prototype for a named hole has a row for the break the judge named, and a scenario of the draft fails it. Commit the prototype when it passes the checks of step 6, and keep its worktree for step 4. Discard it otherwise."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "Run step 1 again for a claim the fix changes. Build the prototype of step 2 for a hole that a judge named with its scenario, its break and its command."
    And the file ".claude/skills/story/SKILL.md" of this repo holds no line that holds "needs proof"
    And the file ".claude/skills/story/SKILL.md" of this repo holds no line that holds "needs a proof"

  Scenario: The Holes judge proves a hole only with its scenario, its break and one command
    Then the file ".claude/skills/story/JUDGES.md" of this repo holds the line "Find each way a build passes every scenario of the draft and still misses the request: a check that accepts too much, a text pinned in part where a reader reads all of it, a sad path the request implies and no scenario states. A hole is proven in a scratch worktree of `main` only when the judge names three things: the scenario, the break that keeps it green, and the one command that settles it. Then report the command and its output, not the code of the proof. A hunch with no such command is reported as a sentence for `## Context`, marked `unproven`."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "A hunch of a judge is marked `unproven`."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "A hunch marked `unproven` goes into `## Context` as it is, and gets no prototype."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "or a hunch marked `unproven`, and no requirement"

  Scenario: The mutation reviewer takes the table of the prototype for its put-back runs
    Then the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "the full SHA of the prototype, from `git rev-parse origin/design/<n>`, when `## Context` names one"
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "and the SHA of the prototype when `## Context` names one"
    And the file ".claude/skills/story/JUDGES.md" of this repo holds the line "When `<SHA>` is the SHA of the prototype, the table of `## Context` stands for the runs with each file put back. The reviewer still makes the first run, to build `tests/Specs/obj` and read `<Class>`, and checks that its `Total` agrees with the table. Then it makes only the narrower mutations below. A file of `git diff --name-only <base>...<SHA>` with no row in the table gets the run with that file put back, as above."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds the line "Done when the first run on `<SHA>` has its `Total`, each changed file has its list of scenarios that stay green or its row in the table of `## Context`, and two or more narrower mutations have theirs."

  Scenario: Each draft judge of the story skill ends on a verdict, and Holes refuses a pin of the program
    Then the file ".claude/skills/story/JUDGES.md" of this repo holds the line "Each judge reports `accept` or `reject`, and `accept` has no fault. Then it reports each fault with the quoted text, the evidence as a path and a line, and the replacement text. Then it gives one line for each thing it found clean."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds the line "A check pins the claim of the story, not the program. A break that the build or the test suite catches is not a hole. `Holes` accepts when each remaining hole needs a change that no scenario of the request implies."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds the line "Done when each hole names the scenario or the line of the draft that closes it, or the change it needs that no scenario of the request implies."

  Scenario: The story skill stops the judges of a draft after round 2
    Then the file ".claude/skills/story/SKILL.md" of this repo holds the line "The run above is round 1. When an angle of round 1 rejects, run the angles again on the fixed draft. This is round 2, and the last round. Act on each fault of round 2 as above, except a hole. A hole of round 2 goes into `## Context` as a line with its scenario, its break, its command and the output, and gets no fix and no prototype. No round 3 runs, and the owner is not asked for one."
    And the file ".claude/skills/story/SKILL.md" of this repo holds the line "Done when each fault of round 1 and of round 2 is fixed in the draft, refuted with its evidence, or, for a hole of round 2, in `## Context`."
    And the file ".claude/skills/story/SKILL.md" of this repo holds no line that holds "Done when each fault is fixed in the draft or refuted with its evidence."

  Scenario: A change the owner states exactly and approves in chat builds with no story
    Then the file ".claude/skills/story/SKILL.md" of this repo holds the line "A change that the owner states exactly and approves in chat builds in that session, with no story and no draft judges. The branch review of step 7 reads the owner's request text as the spec, in place of a body. A story is filed when the owner states a behaviour and wants to read its scenarios before code, or when another session or a batch builds it."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "Start at **Draft** for a new request that needs a story. Start at **Build** at step 6 for a change the owner stated exactly and approved in chat."
    And the file ".claude/skills/story/SKILL.md" of this repo holds no line that holds "A change to this repo goes through two gates."

  Scenario: Holes judges only a draft that holds a check or a pinned text
    Then the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "`Holes` runs only on a draft that holds a check or a text that a scenario pins whole. A plain behaviour draft gets `Claims against main` and `The story`, and no `Holes`."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds the line "This angle runs only on a draft that holds a check or a text that a scenario pins whole. A plain behaviour draft does not get it: its sad paths are the owner's to state, and a builder that can read the repo asks."
