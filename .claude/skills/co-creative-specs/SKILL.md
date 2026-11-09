---
name: co-creative-specs
description: "Spec-driven workflow for co-creative repositories with OpenSpec: when a change needs a spec, how to propose, apply and archive it, and how specs relate to the platform's specs. WHEN: starting a feature, a behaviour change, an architecture decision, or when the user mentions specs, proposals, openspec or /opsx."
metadata:
  author: co-creative
  version: "0.1.0"
---

# Spec-driven work with OpenSpec

Material changes start as a **change proposal** in `openspec/changes/<name>/` before code: proposal (why, what,
impact), design (decisions, risks), specs (requirements with scenarios) and tasks. When the work is done, the change
is archived and its requirements merge into `openspec/specs/`, the living description of the app.

## When to use it

| Use a change | Skip it |
|---|---|
| New feature or capability, behaviour change visible to users | Bug fix restoring specified behaviour |
| Architecture or data-model decision, new Azure resource, new dependency | Refactoring without behaviour change, dependency bumps |
| Anything the platform needs to know about (new job, ceiling, sign-in) | Docs and test-only changes |

## Setup (once per repository)

The repository contains `openspec/config.yaml` (schema `spec-driven`, plus project context). If the OpenSpec agent
skills/commands are missing for your tool, install the CLI and generate them:

```sh
npm install -g @fission-ai/openspec
openspec init --tools claude,codex,github-copilot,opencode   # keeps openspec/ content
```

## Workflow

1. **Propose:** `openspec new change <kebab-name>`, then write the artifacts (`openspec instructions <artifact>
   --change <name>` gives the template). Keep proposals short; state cost, security impact and non-goals.
2. **Validate:** `openspec validate <name>`. Requirements use `SHALL`/`MUST` and at least one `#### Scenario:`.
3. **Agree** with the user before implementing when the change is material.
4. **Apply:** implement task by task; tick `- [x]` in `tasks.md` as you go; update the design if reality differs.
5. **Archive:** `openspec archive <name> -y` when all tasks are done (or explicitly moved to another change).

## Relation to the platform

Platform capabilities are specified in `co-creative/platform` (`openspec/specs/`). An app change that needs a
platform change (new component, ceiling, sign-in, hostname) records that dependency in its proposal and is
coordinated with the platform (see skill co-creative-platform, coordination).
