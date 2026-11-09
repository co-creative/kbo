---
name: co-creative-coding
description: "Architecture and coding guidelines for co-creative .NET apps: pragmatic clean architecture for small apps, C# conventions, Blazor static SSR, testing, error handling and observability. WHEN: designing a feature, structuring projects, writing or reviewing C# / Blazor code, adding tests, refactoring."
metadata:
  author: co-creative
  version: "0.1.0"
---

# Coding guidelines

Small apps, one or two developers, scale to zero, €0 fixed cost. Optimize for **clarity and changeability**, not for
hypothetical scale. The guidelines below adapt well-known principles to that context; the original works are listed
in [references/sources.md](references/sources.md).

## Routing

| Topic | Read |
|---|---|
| Project structure, layering, where code goes | [architecture.md](references/architecture.md) |
| C# style, async, nullability, errors, configuration | [csharp.md](references/csharp.md) |
| Blazor static SSR, accessibility, UI states | [frontend.md](references/frontend.md) |
| Tests: what to test, how, with which tools | [testing.md](references/testing.md) |
| Original books and articles behind these guidelines | [sources.md](references/sources.md) |

For API details, use the .NET skills (`csharp-expert`, `dotnet-webapi`, Blazor, EF Core, `run-tests`) and verify
against Microsoft Learn (MCP server `microsoft-learn`) rather than memory.

## The short version

1. **Dependencies point inward:** domain logic doesn't know about HTTP, Azure SDKs or the database. Adapters do.
2. **Start simple:** one web project plus a test project is fine. Split only when a boundary pays for itself.
3. **Vertical slices over layers-for-their-own-sake:** keep a feature's endpoint, logic and data access close.
4. **Make illegal states unrepresentable:** records, required members, nullable reference types, small value types.
5. **Async all the way with cancellation;** no `.Result`/`.Wait()`.
6. **Fail clearly at the edges, never silently inside.** Validate input, return problem details, log with context.
7. **Every behaviour change comes with a test;** refactor in small steps with green tests.
8. **Observability by default:** structured logs, OpenTelemetry traces and metrics via ServiceDefaults.
