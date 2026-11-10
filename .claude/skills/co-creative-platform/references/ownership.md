# Ownership: app vs. platform

**Rule:** the app owns everything inside its resource group `rg-<slug>`; the platform owns everything outside it and
the guardrails around it.

| The app repository owns (change it here) | The platform owns (ask the platform) |
|---|---|
| Code, tests, Dockerfile(s), `platform.images.json` | Which components exist: the web app, job names and their trigger type |
| `.co-creative.yml`: the delivery flow (`push`, `pr-auto`, `pr-review`) | |
| `platform.app.json`: env settings, CPU/memory, replicas, job schedules and timeouts (container apps); build, test, output and API (static apps) | Sign-in (`auth: entra`, Easy Auth), user/group access |
| `infra/app.bicep`: its own storage (blobs, queues, tables, file shares), SQL free database, Service Bus queues, SignalR Free | Hostnames, DNS, certificates |
| `staticwebapp.config.json` (static apps) | File-share mounts on the shared environment |
| The repository's own docs (`README.md`, `AGENTS.md` app section, `openspec/`) | AI model deployments (shared Foundry) |
| | **Ceilings**: maximum CPU, memory and replicas |
| | Identities, roles, policies, mailbox access |

## Platform-owned files in this repository

`platform.md`, `.github/workflows/platform-deploy.yml`, `.github/platform/deliver.py`,
`.github/platform/platform.app.schema.json`. They're updated only by the platform's syncs.
Don't edit them; if they're wrong, tell the platform (see coordination.md).

## What the app's CI identity can and can't do

- Can: push images, update its own container apps and jobs (image, env, scale, schedule), create/update/delete
  resources in `rg-<slug>`, give its runtime identity data-plane roles on its own resources.
- Can't: create container apps or jobs, change identities or their credentials, touch other resource groups, DNS,
  Entra, or remove locks. Policies deny other resource types, paid SQL and storage keys.
