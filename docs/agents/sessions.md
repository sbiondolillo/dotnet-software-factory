# Sessions and their trust boundaries

What each Managed Agents session of this repo can do. Read it before you add a session kind, or before you change the token, the agent or the environment of one.

A GitHub Actions job starts every session. The job holds the API key `JUDGE_ANTHROPIC_API_KEY` and its own token, `github.token`. No session holds either one. The job reads each answer, decides in code, and writes to GitHub with its own token.

Every session runs on the judge's agent and the judge's environment. Their files are `.github/agents/judge.md` and `.github/agents/judge.environment.yaml`. The agent turns on the `agent_toolset_20260401` tools, with `web_fetch` and `web_search` off. The environment has the networking type `limited`, `allow_package_managers` true, and an empty `allowed_hosts`.

| Session | Token and its scope | Hosts it can reach | Input it does not trust | What it can write | The step that reads its answer |
|---|---|---|---|---|---|
| The reader, `.github/repair/read.py`, at most one session for each red head SHA | `JUDGE_GITHUB_RO_TOKEN`: fine-grained, this repo, Contents, Pull requests and Metadata read. It is the `authorization_token` of the mounted repo. The platform also puts `ANTHROPIC_COMMIT_TOKEN` in the sandbox, to sign commits | the package registries, GitHub through the mounted repo, and `api.anthropic.com`, which answers from every `limited` sandbox | the failed log of the red run, which the prompt holds, and the code at the red SHA | its own sandbox and its checkout. A push fails with 403. Its answer is one JSON object: `class`, `title`, `body`, `evidence` | `Read the red run` in `repair.yml`: `final_answer`, then `parse_answer`. The job files the issue with its own token, as `GH_TOKEN` |
| A judge angle, `.github/judge/judge.py`, one session for each angle of `JUDGE_ANGLES`. The default is two: `Conformance` and `Regressions and style`. `Test strength, by mutation` is the third | the same tokens as the reader | the package registries, GitHub through the mounted repo, and `api.anthropic.com`, which answers from every `limited` sandbox | the code at the head SHA of the pull request, and the spec. The spec is the body of the issue that the pull request closes, or the pull request body | its own sandbox and its checkout. A push fails with 403. Its answer is one JSON object: `angle`, `verdict`, `findings`, `clean` | `Judge the pull request` in `judge.yml`: `final_message`, then `parse_report`. The job computes the verdict, then posts the comment and the check run `judge-verdict` |
| The synthesis, `.github/judge/judge.py`, one session when an angle rejects | none. The session has no mounted repo | none. The session overrides turn off every tool of `agent_toolset_20260401` | the reports of the angles, which quote the code and the output of commands | nothing. Its answer is the review text in Markdown | `Judge the pull request` in `judge.yml`: `final_message`. `render` puts the text above the reports in the comment. The verdict comes from the reports alone |
| The builder | #370 designs it | | | | |
| The feedback session | #370 designs it | | | | |

Read each value off the code and the files under `.github/agents/`. Change this table in the pull request that changes the code it reads.
