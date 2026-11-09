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
- Platform changes to this repository arrive as pull requests (`platform/onboard`, `platform/sync`). Review them
  like any PR: check the diff, let the PR build run, then merge when the operator allows it.

## Etiquette

- Don't perform an action on the platform's behalf that your session wasn't allowed to do; route it to the operator.
- Hold pushes to the default branch when the platform announces a maintenance window, until it says "done".
- After a platform-requested change, report the delivery result (run URL, checks) back.
