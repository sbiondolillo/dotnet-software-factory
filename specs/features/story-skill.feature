Feature: Story skill

  Scenario: The story skill reviews the branch before the pull request
    Then the file ".claude/skills/story/SKILL.md" of this repo holds this text
      """
      ---
      name: story
      description: Turn a requested change to this repo into a story issue, judge its design, and build it once the owner approves. Use when a change to this repo is requested, or when an issue with the label ready is to be built.
      ---

      A change to this repo goes through two gates. The owner approves a **story** on GitHub before any code is written. The owner merges the pull request after it is built. The story says what the system does after the change, at the level of behaviour. The full detail lives in the branch, one time.

      The issue is the **handoff**. Its body is the story. Its comment `## Design` carries everything a build needs. So a session builds from the issue number alone, in any worktree, on any machine.

      Start at **Design** for a new request. Start at **Build** for one issue that has the label `ready`. Start at **Build a batch** to build every story that is ready.

      ## Design

      ### 1. Read `main`

      Read the code on `main` that the change touches. A claim is each fact the design states or relies on: a class, a member, a command, a text a file holds, a test that stays unchanged. Keep the path and the line that holds each one.

      For each text the change replaces or removes, find each scenario that reads it:

      ```
      git grep -n -F '<old text>' origin/main -- specs tests
      ```

      Done when each claim has its line on `main`, and each scenario a hit is in is listed.

      ### 2. Write the design

      Write the design to a file in the session's scratch directory. Step 4 posts it on the issue, and the scratch file is then spent. It opens with the line `## Design`, then the line `` `main` at `<full SHA>` ``, and it holds:

      - each file that changes, and what changes in it
      - each scenario the change adds, and each one it changes or removes, by its name and its feature file
      - each claim, with its line on `main`
      - each full text the change pins, such as a template or a page, as a fenced block
      - each judge's fault from step 3, with its fix or its refutation

      Done when a contributor with no access to this session could build the change from the file alone.

      ### 3. Judge it

      Run the three angles of [`JUDGES.md`](JUDGES.md) → "Judge a design" on the design, as three subagents in parallel. Read each fault against `main` before you act on it. Apply every fix in one pass, and run step 1 again for a claim the fix changes.

      Done when each fault is fixed in the design or refuted with its evidence.

      ### 4. File the story

      The title states the end state in the present tense: "A repo made from the starter holds the template and the rules of a `reframe` issue". The pull request takes the same title. The body has this form:

      ````markdown
      As a <role>, I want to <ability>, so that <benefit>.

      **Today.** <What `main` does now that stops that person, in 3 sentences or fewer.>

      **What changes.** <Each part of the system that changes, named as the code names it, and what it does after the change. Then each invariant, such as "Every other test passes unchanged".>

      ## Scenarios

      ```gherkin
      Scenario: <the name the feature file uses>
        Given <the state>
        When <the event>
        Then <what a person observes>
      ```
      ````

      The role is a real user: a repo owner, a person who runs the console program. Each scenario states what that person observes. A text the change pins in full, such as a template or a page, is named in a `Then` by its path and its key line. The design holds the full text.

      File the story, then post the design as a comment on it:

      ```
      gh issue create --title '<title>' --body-file <story file>
      gh issue comment <n> --body-file <design file>
      ```

      When another story must merge first, mark the new story as blocked by it, in GitHub's issue relationships:

      ```
      gh api repos/{owner}/{repo}/issues/<n>/dependencies/blocked_by -F issue_id=$(gh api repos/{owner}/{repo}/issues/<blocker> -q .id)
      ```

      A design that the owner's comment changes is posted again as a new `## Design` comment. The build reads the last one.

      Done when the issue is filed with no label, its `## Design` comment is posted, and the handover gives its URL and each judge's faults with their fixes. Then stop. The owner reads the story and applies `ready`, or comments a change.

      ## Build

      ### 5. Claim the story and read it

      Claim the issue before any other work. An issue that already has the label `building` belongs to another session: stop, and say so.

      ```
      gh issue edit <n> --add-label building
      ```

      Read the issue and its comments with `gh issue view <n> --comments`. The design is the last comment that starts `## Design`. A later comment of the owner's changes the story. When `main` moved since the SHA the design names, read `git diff <that SHA> origin/main --stat`, and run step 1 again for each claim on a changed file.

      Done when the design matches the issue, every comment of the owner's, and `origin/main`. An issue with no `## Design` comment gets one by steps 1 to 3 first.

      ### 6. Write the scenarios, then the code

      Work in a worktree of your own, on a branch from `origin/main`, and run every later command there:

      ```
      git fetch origin && git worktree add .claude/worktrees/<n>-<a short slug> -b <n>-<a short slug> origin/main
      ```

      Each scenario of the issue goes into a feature file under `specs/features/` by its name, with each of its `Given`, `When` and `Then` lines. The feature file adds the detail the issue names by path: a doc string with the full text, a table. Run `dotnet test` and see each new scenario fail. Then write the code until it passes.

      Then run every check of the `build` job of `.github/workflows/ci.yml` on the branch:

      ```
      dotnet restore --locked-mode && dotnet build --no-restore && dotnet format --verify-no-changes --no-restore && dotnet test --no-restore
      ```

      A change to code that calls GitHub, `docker` or Claude Code also runs `dotnet run --no-restore --project tests/Integration`, and the result goes in the pull request.

      Commit the branch, new files included, so that a reviewer reads every change by one SHA:

      ```
      git add -- <each path of "Files that change"> && git commit -m '<the issue title>'
      ```

      Done when every check above passes, the result of the integration suite is kept when that suite ran, and `git status --porcelain` lists no path of "Files that change".

      ### 7. Review the branch

      Run the three angles of [`JUDGES.md`](JUDGES.md) → "Judge a branch" on the commit, as three subagents in parallel, on Opus. The branch is not pushed, so each reviewer clones this worktree into a scratch directory of its own and checks the SHA out there. Give each one:

      - the full SHA of the commit, from `git rev-parse HEAD`
      - the full SHA of the base, from `git fetch -q origin && git rev-parse origin/main`
      - the path of this worktree
      - the issue number, and the path of a file that holds its body and its last `## Design` comment

      Read each finding against the branch before you act on it. Each finding gets a fix on the branch or a refutation with its evidence. A fix is a new commit, and runs the checks of step 6 again.

      Done when each angle accepts, or each finding has a fix or a refutation.

      ### 8. Open the pull request

      Check the link from the story to the code: each scenario name of the issue is in `specs/features/`.

      ```
      gh issue view <n> --json body -q .body | tr -d '\r' | sed -nE 's/^ *Scenario( Outline)?: //p' | while read -r name; do grep -rhE '^ *Scenario( Outline)?: ' specs/features | sed -E 's/^ *Scenario( Outline)?: //' | grep -qxF -- "$name" || echo "missing: $name"; done
      ```

      Push the branch, and open the pull request with the issue's title. The body states what changed, the result of each suite, each reviewer's verdict, each finding with its fix or its refutation, and the line `Closes #<n>`.

      ```
      gh pr create --title '<the issue title>' --body-file <file>
      ```

      Done when the check above prints nothing, the pull request is open, its body holds each reviewer's verdict and each finding with its fix or its refutation, and its checks are green. The owner merges it. The worktree stays until the merge, for a fix the owner asks for. After the merge, `git worktree remove .claude/worktrees/<n>-<slug>` removes it.

      ## Build a batch

      List the stories that are ready to build: the label `ready`, no label `building`, and no open issue that blocks it.

      ```
      gh api graphql -F owner='{owner}' -F name='{repo}' -f query='query($owner:String!,$name:String!){repository(owner:$owner,name:$name){issues(states:OPEN,labels:["ready"],first:100){nodes{number title labels(first:20){nodes{name}} blockedBy(first:20){nodes{number state}}}}}}' -q '.data.repository.issues.nodes[] | select([.labels.nodes[].name] | index("building") | not) | select([.blockedBy.nodes[] | select(.state == "OPEN")] | length == 0) | "\(.number)\t\(.title)"'
      ```

      The query reads each blocker's state, and leaves out a story that an open issue blocks. It waits for the next batch. `issueDependenciesSummary` lags a new relationship by some seconds, so the query reads the `blockedBy` list itself.

      Give each story that is ready to its own builder: a subagent with its own worktree, or a separate session. Give it one line, `Build #<n> with the story skill.`, and nothing more. The issue holds the rest. Two stories that change the same file are built one after the other, in the order of their numbers.

      Done when each story that is ready has a builder, and the handover lists each one with its builder and, once open, its pull request.
      """

  Scenario: The judges of the story skill cover a design and a branch
    Then the file ".claude/skills/story/JUDGES.md" of this repo holds this text
      """
      # Judges

      Reference for [`story`](SKILL.md). "Judge a design" is step 3, and "Judge a branch" is step 7. Each angle is one subagent. A judge is read-only: it edits no tracked file, and it files and posts nothing.

      ## Judge a design

      Give each judge the path of the design, the request in the owner's words, and the `main` SHA.

      Each judge reports each fault with the quoted text, the evidence as a path and a line, and the replacement text. Then it gives one line for each thing it found clean.

      ### Claims against `main`

      Read each claim of the design against the line on `main` that holds it. Read each new scenario against `main`, and trace what `main` does after its event: a scenario `main` already passes is a fault. For each text the change replaces or removes, run `git grep -n -F` over `specs` and `tests`, and check that the design names each scenario a hit is in. For each stream or file the change adds lines to, find each test that counts its lines.

      Done when each claim names its line on `main`, and each test the change makes false is named.

      ### Holes

      Find each way a build passes every scenario of the design and still misses the request: a check that accepts too much, a text pinned in part where a reader reads all of it, a sad path the request implies and no scenario states. Prove a likely hole in a scratch work tree of `main` when a command can show it.

      Done when each hole names the scenario or the line of the design that closes it.

      ### The story

      Read the design as the owner reads the story. The story asks for the change the request asks for, and no more. Each scenario states what a real user observes, in the names the code and the tools use. Each change of the design is reached by a scenario, or is named as an invariant.

      Done when each sentence of the story and each scenario has been read against the request and the design.

      ## Judge a branch

      Give each reviewer the SHA of the commit, the SHA of the base, the path of the build worktree, and the path of the file that holds the issue and its design. The reviewer works in a clone of its own, and edits nothing in the build worktree. `<scratch>` is an absolute path of its own, `<clone>` is `<scratch>/clone`, and `<HOME>` is `<scratch>/home`:

      ```
      mkdir -p <scratch>/home && git clone -q <worktree> <scratch>/clone && cd <scratch>/clone && git checkout -q <SHA>
      ```

      Run every later command in `<clone>`. The clone's `origin/main` is the local `main` of the checkout, which can lag the base. So each command below takes `<base>`, never `origin/main`. The base can be ahead of the commit the branch started from. So a list of the branch's changes takes three dots, `<base>...<SHA>`, and `<fork>` is `git merge-base <base> <SHA>`.

      Each reviewer reports `accept` or `reject`. Then it reports each finding with the file and the line, the fault in one sentence, the evidence as a command and its output, and a fix. A finding with no evidence is not a finding. A nitpick is marked `nitpick`. Then it gives one line for each thing it found clean.

      ### Conformance

      Read each sentence of `**What changes.**` of the issue and each row of the table "Files that change" of the design against the clone, and give the line of the branch that makes it true. Save each full text of the design to a file, and compare it with its file in the clone: `cmp <saved> <path>`. Read each scenario of the issue against its feature file: the same name, and the same steps in the same order.

      Done when each sentence, each row, each text and each scenario has its line in the branch or its finding.

      ### Test strength, by mutation

      Run the features the story names once on `<SHA>`, before any mutation. The run prints `Passed!`, with a `Total` equal to the count of scenarios in those features. A run that prints `No test matches the given testcase filter`, or that fails to restore or to build, is a fault of the command, not a result.

      ```
      docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp/home -v <HOME>:/tmp/home -v <clone>:<clone> -w <clone> mcr.microsoft.com/dotnet/sdk:<major.minor> sh -c 'dotnet restore --locked-mode && dotnet test --no-restore --filter "FullyQualifiedName~<Class>"'
      ```

      The image tag is the major and minor version of `global.json`, such as `10.0`. Docker makes a missing `<HOME>` owned by root, and the restore then fails, so the clone block makes it first. Each run uses the same `<HOME>`, so a restore reads the package cache of the run before it. `<Class>` is the class Reqnroll compiles for the feature, and punctuation changes it: the title `A repo made from the starter holds the skill write-issue` gives `ARepoMadeFromTheStarterHoldsTheSkillWrite_IssueFeature`. Read it after the first run:

      ```
      grep -rhoE 'partial class \w+' tests/Specs/obj --include='<file>.feature.cs'
      ```

      Then list the changed files: `git diff --name-only <base>...<SHA>`. For each one, put the version of `<fork>` back on its own, run the features, and list each pinned scenario that stays green. Then restore the commit. A file that is new in the change is removed with `git rm -q <path>` in place of the checkout from `<fork>`. A run with the feature file itself removed has no scenario, which is its result.

      ```
      git checkout <fork> -- <path>
      <the docker run above>
      git reset -q --hard <SHA>
      ```

      A scenario that stays green with a file of the change put back is a finding: it pins nothing in that file.

      Then make two or more narrower mutations of the new code. Each one is an edit that the controls of the design do not cover: a bound off by one, a branch inverted, a line of a pinned text changed. Run the features for each, and restore the commit after each. A mutation that no scenario fails is a finding, with the scenario that should fail it.

      Done when the first run on `<SHA>` has its `Total`, each changed file has its list of scenarios that stay green, and two or more narrower mutations have theirs.

      ### Regressions and style

      - A changed scenario or binding that the story does not name: `git diff --stat <base>...<SHA> -- specs tests`, read against the table "Files that change".
      - A caller of removed code, and a scenario of a removed binding: `git grep -n -F '<name>' <SHA>` and `git grep -n -F '<step text>' <SHA> -- specs`.
      - A step-text collision: a new step text that a binding of `<base>` already matches, a regex binding included. The new step texts are `git diff -U0 <base>...<SHA> -- specs | grep -E '^\+ *(Given|When|Then|And|But) '`, read against `git grep -n -E '\[(Given|When|Then)\(' <base> -- tests`.
      - House style: each added line of `git diff -U0 <base>...<SHA> -- src tests`, read against the neighbouring files and `AGENTS.md` → "The rules that build and style enforce".
      - A clean merge: `git merge-tree --write-tree <base> <SHA>` exits 0.

      Done when each item has its command and its output, and each hit is a finding or is marked clean.
      """
