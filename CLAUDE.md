@AGENTS.md

In a session with the owner, a change to this repo follows the `story` skill. The owner starts it with `/story`. Otherwise the session reads the file `.claude/skills/story/SKILL.md` and follows it, and does not call the Skill tool for `story`. The skill files the change as a story issue on GitHub, builds it on a branch once the owner labels it `ready`, and opens the pull request. The owner merges.

A factory run is `claude -p` in the factory's sandbox, with the work tree at `/work`. It follows the factory's prompt and does not use the `story` skill. It changes the work tree in place. The factory commits, pushes, and opens or updates the pull request.
