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
