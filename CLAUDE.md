@AGENTS.md

In a session with the owner, a change to this repo follows the `story` skill. The owner starts it with `/story`, or the session reads `.claude/skills/story/SKILL.md` and follows it. The skill files the change as a story issue on GitHub, builds it on a branch once the owner labels it `ready`, and opens the pull request. The owner merges.

A factory run follows the factory's prompt and does not use the `story` skill. It builds in place, and the factory commits the change and opens the pull request.
