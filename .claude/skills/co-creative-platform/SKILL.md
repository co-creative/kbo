---
name: co-creative-platform
description: "The co-creative platform model for app repositories: what the app owns vs. what the platform owns, the runtime contract, delivery, configuration (platform.app.json), own Azure resources (infra/app.bicep), static apps, the co-creative NuGet feed for reusable packages, and how to request platform changes. WHEN: changing settings, scale, schedules, Azure resources, Dockerfile, health endpoint, deployment, hostnames, sign-in, or anything touching Azure; before writing reusable code (look for a package) or when sharing code between apps (publish one); when a task needs something the app can't do itself."
metadata:
  author: co-creative
  version: "0.4.0"
---

# co-creative platform

Apps run on one shared, cheap, secretless Azure platform. The platform repository `co-creative/platform` describes
each app in `apps/<slug>/main.bicepparam`; the app repository owns its code and part of its configuration.

## Routing

| Task | Read |
|---|---|
| What may I change here, what must I ask for? | [ownership.md](references/ownership.md) |
| Health endpoint, port, Dockerfile, env vars, identity | [contract.md](references/contract.md) |
| Settings, scale, schedules (`platform.app.json`); own Azure resources (`infra/app.bicep`) | [configuration.md](references/configuration.md) |
| How a push becomes a deployment; why a delivery failed | [delivery.md](references/delivery.md) |
| Static sites (Static Web Apps) | [static-apps.md](references/static-apps.md) |
| Reusable libraries: find, consume or publish a package on the co-creative NuGet feed (e.g. the design system `CoCreative.UI`) | [packages.md](references/packages.md) |
| Asking the platform for a change; working with other sessions | [coordination.md](references/coordination.md) |

## Five facts to keep in mind

1. Every push to the default branch is a production delivery; pull requests only build and test.
2. No secrets anywhere: the app reaches Azure with its managed identity.
3. Container apps scale to zero by default: the first request after idle takes a few seconds; in-memory state
   (caches, Data Protection keys) doesn't survive.
4. The app can manage resources only inside its own resource group `rg-<slug>`, within platform policies (allowed
   resource types, free SQL only, no storage keys).
5. Platform-owned files in this repository change only through `platform/sync` pull requests (merged by the
   platform).
