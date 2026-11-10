# Working with the platform

The platform is maintained in `co-creative/platform`, often by a separate agent session ("platform session")
working with the same operator.

## When to involve the platform

- A new component (web app, job), a different trigger type (schedule, manual, queue).
- Sign-in changes, user/group access, group claims.
- Hostnames, DNS, certificates, file-share mounts, AI model deployments.
- Higher ceilings (CPU, memory, replicas).
- Anything that fails because of identity, role, policy or federated credential.
- Platform-owned files that seem wrong or outdated.

## How

- If a platform session is reachable (cross-session messaging), message it: state the app, what you need, why, and
  the evidence (run URL, error text). Keep the first line self-contained.
- Otherwise open an issue in `co-creative/platform`, or ask the operator.
- Platform changes to this repository arrive as `platform/sync` pull requests, which the platform merges itself.
  If a sync breaks the build or contradicts this repository, tell the platform; don't patch platform-owned files
  yourself.

## Etiquette

- Don't perform an action on the platform's behalf that your session wasn't allowed to do; route it to the operator.
- Hold pushes to the default branch when the platform announces a maintenance window, until it says "done".
- After a platform-requested change, report the delivery result (run URL, checks) back.
