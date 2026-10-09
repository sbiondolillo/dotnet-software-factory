# Prove a check

Reference for step 4 of [`write-issue`](SKILL.md), and for the angle `Holes` of [`REVIEW.md`](REVIEW.md). The proof runs the check of the draft in a copy of `main`, once for each run of a table, and reads the exit status of each run. The rules of a check are `## A check` of `docs/write-an-issue.md`.

## The copy

The factory runs the check with `sh -c` in a `git worktree` of the change, with the network on. A worktree holds the tracked files only. The proof uses the same shape. `<PROOF>`, `<CHECK>` and `<HOME>` are absolute paths outside the repo.

Save the check of the draft to `<CHECK>`. This program reads the first fenced block under `## Check` as the factory reads it:

```
awk '{ sub(/\r$/, ""); t = $0; sub(/^[ \t]+/, "", t); sub(/[ \t]+$/, "", t) }
m != "" { if (t ~ ("^" substr(m, 1, 1) "+$") && length(t) >= length(m)) { if (s) { if (c ~ /[^ \t\n]/) printf "%s", c; exit } m = "" } else if (s) c = c $0 "\n"; next }
t ~ /^#+[ \t]/ && t !~ /^#######/ { if (s) exit; s = (t == "## Check"); next }
match(t, /^(```+|~~~+)/) { m = substr(t, 1, RLENGTH); if (m ~ /^`/ && index(substr(t, RLENGTH + 1), "`")) m = "" }' <draft> > <CHECK>
```

Read `<CHECK>`: it holds the lines of the block and nothing else. An empty `<CHECK>` tells that the factory reads no check: fix the block of the draft. Then make the home directory and the copy:

```
mkdir -p <HOME> && git fetch -q origin && git worktree add --detach <PROOF> origin/main
```

A `docker run` mounts a missing path as a directory that `root` owns. The restore is then refused, or the run prints nothing and exits with the code 0. So `<CHECK>` and `<HOME>` exist before the first run.

Reset the copy to the `main` SHA before each run:

```
git -C <PROOF> reset -q --hard <main SHA> && git -C <PROOF> clean -qfdx
```

Then make the change of the row. A change that runs `dotnet`, such as `dotnet format`, runs in the image:

```
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp/home -v <HOME>:/tmp/home -v <PROOF>:<PROOF> -w <PROOF> mcr.microsoft.com/dotnet/sdk:<major.minor> dotnet format
```

It leaves `obj/` and `bin/`, and the factory's copy has none. Remove them with `git -C <PROOF> clean -qfdX`, then plant the file of the row, if it has one.

Run the check in the SDK image of `global.json`: the tag is its major and minor version, such as `10.0`. The run mounts the git directory of the repo and the copy at their own paths, so `git` in the copy reads the repo. Each run uses the same `<HOME>`, so a restore reads the package cache of the run before it.

```
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp/home -v <HOME>:/tmp/home -v "$(git rev-parse --path-format=absolute --git-common-dir):$(git rev-parse --path-format=absolute --git-common-dir)" -v <PROOF>:<PROOF> -v <CHECK>:/check.sh:ro -w <PROOF> mcr.microsoft.com/dotnet/sdk:<major.minor> sh -x /check.sh; echo "exit $?"
```

The trace of `sh -x` ends at the command that stopped the run. Read it for each run that must fail. Remove the copy when the table is complete:

```
git worktree remove --force <PROOF>
```

## The runs

| Run | The change in the copy | Must exit |
|---|---|---|
| `main` | none | not 0, and not 126 or 127, at the first line that reads the change |
| faked | the cheapest change that meets the prose: a move by cut and paste with no fix of a `using`, or the smallest edit of a test that meets the prose. The tests can fail | 0 |
| other means, one for each choice | the faked change with another helper, another form of an assertion or another form of a count, for each choice that the prose leaves to the build | 0 |
| partial, one for each element | the faked change minus one element, as bullet 1 of `## A check` lists them | not 0, at the line of that element |

The exit codes 126 and 127 tell that the check cannot run: fix the check. A partial run that exits with the code 0 is a hole: add a line to the check, and run the table again. An "other means" run that exits with a code other than 0 tells that the check reads a means: take that line out of the check.

## A check that raises an analyzer rule

The check has the parts of `docs/write-an-issue.md` → `## A check`, and its proof takes this table in place of the table above:

| Run | The change in the copy | Must exit |
|---|---|---|
| `main` | none | not 0, at the `sha256sum -c` |
| before | the settings files as the prose states them, and no code changed | not 0, at the first count of part 7, or at the build when the check has no count |
| faked | before, with each finding fixed by `dotnet format` or by a script | 0 |
| hole, one for each form | faked, with one form planted: each hiding form of part 5, one generated name of part 6 in another case, such as `Greeter.Designer.cs`, one new settings file of part 4, such as `Directory.Build.rsp`, and one `*.props` file under an `obj/` of part 1 | not 0, at the line of its part |
| dodge, one for each pinned line | faked, with the pinned line rewritten to dodge the rule, and a comment that pads its count | not 0, at the line pin |

A build stops at the first project that fails, so the projects that depend on it give no findings. To list every finding of every project, run `dotnet build -p:TreatWarningsAsErrors=false` once in the copy with the change of the "before" row. The "before" run itself runs the check as written.

## The proof file

The proof file is the comment that step 6 posts. It names the full `main` SHA and the image, and holds one row for each run:

| Run | The change | Exit | Stops at |
|---|---|---|---|
| `main` | none | 1 | `sha256sum -c` |

Done when each run of the table gives the status that its row asks for, on one `main` SHA.
