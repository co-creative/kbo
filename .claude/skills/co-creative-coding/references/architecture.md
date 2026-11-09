# Architecture for small apps

## The dependency rule, applied pragmatically

Business rules sit at the centre and depend on nothing technical; frameworks, Azure SDKs, databases and UI are
details that depend on the centre (after Martin's *Clean Architecture* and Cockburn's ports and adapters). In a small
app that does **not** mean four projects per feature. It means:

- Domain types and rules are plain C# (records, methods), testable without Azure or ASP.NET Core.
- Access to the outside world goes through a small interface **defined by the consumer** (for example
  `IMailClient`, `IReleaseStore`), implemented in an adapter that uses the SDK. Register adapters in DI.
- Endpoints/pages are thin: parse input, call the feature, map the result.

## Project layout (template `dotnet-aca-v1`)

| Project | Holds |
|---|---|
| `App.Web` | Endpoints, Razor components, composition root (`Program.cs`) |
| `App.Job` | Job entry point(s); runs once and exits |
| `App.Platform` | Platform glue: settings, Easy Auth principal, managed-identity clients, `/health` |
| `App.ServiceDefaults` | OpenTelemetry, health checks, resilience defaults |
| `App.AppHost` | Local orchestration (Aspire); never used for deployment |
| `tests/App.Tests` | Unit and integration tests |

Add a `App.Domain` (or `App.Core`) project only when domain logic is substantial and shared by web and job. Prefer
**feature folders** (`Features/Lookup/…`) inside a project over technical folders (`Services/`, `Helpers/`).

## Domain modelling

- Use the language of the domain in type and method names (Evans' *ubiquitous language*); keep it consistent with
  the specs in `openspec/`.
- Value objects (records) for concepts with rules (`EnterpriseNumber`, `Iban`), entities only where identity matters.
- Tactical DDD patterns (aggregates, domain events) only when the domain is complex enough; most small apps need a
  few well-named types and pure functions.

## Boundaries and coupling

- One app, one resource group: no shared databases with other apps. Integrate through HTTP APIs or queues.
- Keep transactions inside one component; jobs that publish results do so atomically (write to a temp name, then
  rename/swap).
- Configuration is data (`platform.app.json` env), not code branches per environment: there's one environment.

## Avoid

- Speculative abstractions (generic repositories over EF Core, mediator pipelines for three endpoints).
- Static mutable state, service locator, `IServiceProvider` lookups in business code.
- Leaking SDK types (`BlobClient`, `DbContext`) into domain logic.
