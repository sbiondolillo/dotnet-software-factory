# Judge a design

Reference for step 3 of [`story`](SKILL.md). Each angle is one subagent. Give it the path of the design, the request in the owner's words, and the `main` SHA. A judge is read-only: it edits no tracked file, and it files and posts nothing.

Each judge reports each fault with the quoted text, the evidence as a path and a line, and the replacement text. Then it gives one line for each thing it found clean.

## Claims against `main`

Read each claim of the design against the line on `main` that holds it. Read each new scenario against `main`, and trace what `main` does after its event: a scenario `main` already passes is a fault. For each text the change replaces or removes, run `git grep -n -F` over `specs` and `tests`, and check that the design names each scenario a hit is in. For each stream or file the change adds lines to, find each test that counts its lines.

Done when each claim names its line on `main`, and each test the change makes false is named.

## Holes

Find each way a build passes every scenario of the design and still misses the request: a check that accepts too much, a text pinned in part where a reader reads all of it, a sad path the request implies and no scenario states. Prove a likely hole in a scratch work tree of `main` when a command can show it.

Done when each hole names the scenario or the line of the design that closes it.

## The story

Read the design as the owner reads the story. The story asks for the change the request asks for, and no more. Each scenario states what a real user observes, in the names the code and the tools use. Each change of the design is reached by a scenario, or is named as an invariant.

Done when each sentence of the story and each scenario has been read against the request and the design.
