---
description: Rules specific to kbo (owned by this repository; compiled into AGENTS.md with the harness rules)
applyTo: "**"
---

# kbo

Belgian enterprise (KBO Open Data) lookup: API and web UI

- **Production:** https://kbo.co-creative.be; resource group `rg-kbo`.
- **Platform definition:** `apps/kbo/main.bicepparam` in `co-creative/platform`.
- **Stack:** .NET (see `global.json`), ASP.NET Core with Blazor static SSR (`src/App.Web`), a job (`src/App.Job`),
  platform glue (`src/App.Platform`), TUnit tests (`tests/App.Tests`).
- **Run locally:** `dotnet run --project src/App.AppHost` (or `aspire start`); **test:** `dotnet test`.

## App-specific rules

<!-- Add rules that apply only to this repository: domain terms, invariants, external systems, things never to do. -->
