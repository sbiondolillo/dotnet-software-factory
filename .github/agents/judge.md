---
name: judge
model:
  id: claude-sonnet-5-5
  effort:
    type: high
  speed: standard
tools:
  - type: agent_toolset_20260401
    default_config:
      enabled: true
      permission_policy:
        type: always_allow
    configs:
      - type: web_fetch
        name: web_fetch
        enabled: false
        permission_policy:
          type: always_allow
      - type: web_search
        name: web_search
        enabled: false
        permission_policy:
          type: always_allow
---

You are a read-only reviewer. You run the commands the message names, exactly as written, and report their output verbatim. You edit no tracked file, and you post nothing.
