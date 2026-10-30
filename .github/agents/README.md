# Agents and environments

The Managed Agents definitions of this repo, in the form that `ant apply` reads. `docs/agents/sessions.md` says what each session can do with them.

| file | what it is |
|---|---|
| `judge.md` | the judge's agent: the model, the tools and the system prompt. Its id is the repository variable `JUDGE_AGENT_ID` |
| `judge.environment.yaml` | the judge's environment: the networking and the packages. Its id is the repository variable `JUDGE_ENVIRONMENT_ID` |
| `claude-lock.json` | the lockfile: the id of each file's resource, and the organization and the workspace that hold them |

The reader of `.github/repair/read.py` uses the same agent and the same environment. The builder's agent and environment come with #370.

## Check the files against the live resources

Install the `ant` CLI, 2.4.0 or later, as the CLI quickstart says. Put the API key of the dedicated workspace in `ANTHROPIC_API_KEY`. Then run this from the root of the repo:

```
ant apply --lock-file .github/agents/claude-lock.json < /dev/null
```

The pass condition is the line `Everything is up to date.` and the exit code 0. `ant apply` finds no `claude-lock.json` at the root, so the command names it. Each id in the lockfile must equal its variable. `gh variable list` shows the two variables.

## The lockfile was written by hand

The Console created the agent and the environment on 2026-10-27. `ant apply` adopts no resource that it did not create. So the lockfile was written by hand, with the kind, the id and the agent's version, and no hashes. With no recorded hash, each plan compares the file field by field with the live resource. A change in the Console to the agent raises its version, and the plan refuses to apply until `--force`. A change to the environment shows as an update, and `ant apply --yes` would overwrite it with the file.

To change the agent or the environment, edit its file and run `ant apply` with no `< /dev/null`. Read the plan before you approve it. The first real apply writes the hashes to the lockfile. Commit the lockfile in the same pull request.
