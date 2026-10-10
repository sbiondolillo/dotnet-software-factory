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
    Then the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "A **prototype** is built for two reasons only: a change to a text that a scenario pins, so the owner reads a diff, or a hole that a judge named with its scenario, its break and its command."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "Any other draft has no prototype. Code is not such a text: a scenario pins what code does."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "Run step 1 again for a claim the fix changes. Build the prototype of step 2 for a hole that a judge named with its command."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "A prototype for a named hole has a row for the break the judge named, and a scenario of the draft fails it."
    And the file ".claude/skills/story/SKILL.md" of this repo holds no line that holds "needs proof"
    And the file ".claude/skills/story/SKILL.md" of this repo holds no line that holds "needs a proof"

  Scenario: The Holes judge proves a hole only with its scenario, its break and one command
    Then the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "only when the judge names three things: the scenario, the break that keeps it green, and the one command that settles it"
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "A hunch with no such command is reported as a sentence for `## Context`, marked `unproven`."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds no line that holds "when a command can show it"
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "A hunch of a judge is marked `unproven`."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "A hunch marked `unproven` goes into `## Context` as it is, and gets no prototype."

  Scenario: The mutation reviewer takes the table of the prototype for its first run and its put-back runs
    Then the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "The rows are the prototype as it is, each changed file of the base put back on its own, and each narrower mutation."
    And the file ".claude/skills/story/SKILL.md" of this repo holds a line that holds "the full SHA of the prototype, from `git rev-parse origin/design/<n>`, when `## Context` names one"
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "When `<SHA>` is the prototype's SHA that the caller gives, the table of `## Context` stands for the first run and for the runs with each file put back."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "The reviewer reads the table, and makes only the narrower mutations below."
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "with no row in the table gets its put-back run as above"
    And the file ".claude/skills/story/JUDGES.md" of this repo holds a line that holds "or the table of `## Context` stands for it"
