---
paths:
  - "**"
---

# Delivery flow

Every co-creative repository picks its delivery flow in `.co-creative.yml` at the repository root. The repository
owns that file and changes it with an ordinary commit:

```yaml
delivery: push   # push | pr-auto | pr-review
```

No file, no `delivery` key or an unknown value means **`push`**. Read the file before your first commit in a session.

## Always, whatever the flow

- **Run the tests locally before every push** (`dotnet test`, or the repository's test command named in its
  README/AGENTS.md). Don't push while they fail; if a failure isn't yours, say so and stop.
- CI runs the tests again before anything deploys. A red CI run is yours to fix first.
- Conventional Commit messages; one logical change per commit.

## `push` (default)

1. Run the tests, then commit on the default branch (`main` or `master`).
2. Push. In an app repository every push to the default branch is a production delivery.
3. Watch the CI run (`gh run watch`, or `gh run list --limit 1`). If it fails, forward-fix with a new commit; don't
   rewrite pushed history.

## `pr-auto`

1. Create a branch (`<type>/<short-name>`, e.g. `fix/health-timeout`), run the tests, commit and push the branch.
2. `gh pr create --fill` (or with a short title and body: what, why, how you tested it).
3. Wait for the checks (`gh pr checks --watch`). Pull requests only run the tests; nothing deploys.
4. When they pass, `gh pr merge --squash --delete-branch`. That merge is the production delivery; watch its run as
   with `push`.
5. When a check fails, fix it on the branch and push again. Never merge a red PR.

## `pr-review`

As `pr-auto` up to and including step 3. Then **stop**: report the PR link and the check result, and leave the
merge to a person. Don't merge, even when the checks pass or someone asks another session to do it.

A direct push to the default branch in a `pr-*` repository isn't blocked (GitHub Free has no branch protection for
private repositories): CI deploys it and records a warning. Don't rely on that; follow the flow.
