---
description: Core rules for every co-creative app repository (compiled into AGENTS.md / CLAUDE.md by apm compile)
applyTo: "**"
---

# co-creative app repository

This repository is an app on the **co-creative platform** (Azure Container Apps or Static Web Apps, one Azure
subscription, budget €10/month for the base platform). The platform itself lives in `co-creative/platform`.

## Non-negotiable rules

1. **No secrets, ever.** Never commit, log or print keys, connection strings, tokens or passwords. Azure access uses
   the app's managed identity (`AZURE_CLIENT_ID`; `ManagedIdentityCredential` in Azure, `DefaultAzureCredential`
   locally). If something seems to need a secret, stop and ask: the platform probably offers a keyless way.
2. **Respect the ownership split.** This repository owns its code, `platform.app.json` (settings, scale, schedules)
   and `infra/app.bicep` (its own storage, SQL, Service Bus, SignalR) and `.co-creative.yml` (its delivery flow).
   The platform owns which components exist, sign-in, hostnames, DNS, mounts, AI and the ceilings. Platform-owned
   files here (`platform.md`, `.github/workflows/platform-deploy.yml`, `.github/platform/*`) change only through the
   platform's syncs: never edit them.
3. **`main` (or `master`) is production.** Every push deploys after the CI tests pass. Run the tests locally before
   every push and follow the repository's delivery flow (`.co-creative.yml`, default `push`; see the delivery
   rules). Changing `platform.app.json` or `infra/app.bicep` is a production change: say so before doing it.
4. **Keep the runtime contract** (see `platform.md`): one Dockerfile target per component, no command/args
   overrides. The **web** component serves HTTP on port 8080 and `GET /health` returns 200 as soon as the process
   runs (also without data). **Jobs** have no port or health endpoint: they run once and exit, with exit code 0 for
   success; a non-zero exit marks the execution failed.
5. **Spec first for material changes.** New features, behaviour changes and architecture decisions go through the
   spec-driven workflow (`openspec/`), before code.

## Where to look

- `platform.md`: this app's platform contract, delivery steps and what the platform provides.
- Skill **co-creative-platform**: the platform model, ownership, delivery and how to work with the platform team.
- **Reusable packages:** the private co-creative NuGet feed (GitHub Packages) holds shared libraries such as the
  design system `CoCreative.UI`. Look there before building something reusable; publish code there when two or
  more apps need it (`CoCreative.*` names). See skill co-creative-platform, packages.
- Skill **co-creative-coding**: architecture and coding guidelines (C#/.NET, Blazor, tests).
- Skill **co-creative-specs**: the spec-driven workflow with OpenSpec.
- .NET skills (`csharp-expert`, `csharp-refactoring`, `dotnet-webapi`, Blazor, EF Core, testing, .NET 11) and Azure
  skills (`azure-*`): use them for their topics instead of guessing APIs.
- MCP servers: **microsoft-learn** for Microsoft and Azure documentation (verify APIs and limits there; don't rely on
  memory for SDK signatures), **azure** for read-only inspection of Azure resources.

## Working style

- Small, reviewable changes; run `dotnet test` (or the repository's test command) before declaring work done.
- Report outcomes faithfully: say what you verified and what you didn't.
- Use Conventional Commit prefixes (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).
