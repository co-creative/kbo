---
paths:
  - "**"
---

# Context management

Long sessions build up context: every file, log and search result you read stays in the conversation and makes the
rest of the work slower, costlier and less precise. Keep your own context small.

## Delegate heavy reading to a subagent; keep only its conclusion

Use your tool's subagent mechanism (a subagent, task agent or custom agent) for work where the reading is large
and the answer is small:

- **Broad searches** across many files, directories or naming conventions.
- **Logs:** build, test and CI output (`gh run view --log-failed`, long `dotnet test` output).
- **Reviews** of a diff or a set of files.
- **Research** in documentation or in other repositories.

The subagent starts without your context, so give it a precise brief: the goal, the files or places to look, what
you've already ruled out, and the form of the answer you need (e.g. "the failing test, the assertion, file:line").
Keep its conclusion; don't copy its raw output into your own context.

## Do small, known things yourself

A lookup or edit whose target you already know (one file, one symbol, one command) costs less done directly.
Delegation isn't free: a subagent rereads everything it needs. When in doubt, don't delegate.

## One task per conversation; the repository is the memory

When a task is done, record where you stand **in the repository**: tick it in `tasks.md`, commit, and note open
items in the OpenSpec change (or the issue). Then say that the conversation can be cleared or restarted (`/clear` in
Claude Code, a new session or chat in the other tools). The next task starts from the repository, not from this
conversation's history.

## No multi-agent fan-out unless asked

Workflows that fan out over many agents in parallel cost a lot of tokens. Use them only when the user explicitly
asks for them.
