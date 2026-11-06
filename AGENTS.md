# AGENTS.md

Guidance for AI coding agents working in this repository.

## What this is

A .NET 11 solution for the Belgian **KBO open data** (Kruispuntbank van Ondernemingen, the crossroads bank for enterprises): look up and search all ~2M Belgian enterprises.

**Rewrite in progress.** Read `specs/azure-architecture.md` first. Each monthly KBO "Full" zip becomes **one immutable, read-only SQLite file** (`kbo-<extract>.db`) built by a Container Apps job. A single scale-to-zero web app (minimal API + static-SSR Blazor UI) reads that file directly from an Azure Files share. There's no incremental import and no database server.

- **Done:** phase 1 (the core library and the build tool) and phase 2 (the web app).
- **Next:** phase 3 (Azure) and phase 4 (deleting the legacy projects).

The official data-model description is `Net.Code.Kbo.Data/Cookbook-KBO-Open-Data.pdf`. The data isn't in the repo: download it from kbopub.economie.fgov.be after creating an account.

## Solution layout

| Project | Role |
|---|---|
| `Net.Code.Kbo` | Core library: `KboNr`/`EntityNumber`, the company document model (`Documents.cs`, JSON source-generated via `KboJson`), `CodeBook`, `SearchQuery` (input → FTS5 MATCH), `CompanyStore` (read-only queries on a release db). |
| `Net.Code.Kbo.Build` | Console tool, assembly `kbo`: `build`, `release` (the job), `get`, `search`. Turns a KBO zip into a release db. |
| `Net.Code.Kbo.Web` | The web app: JSON API under `/api` (`CompanyApi.cs`) plus a Blazor UI with **static SSR only** (no interactive render modes), both using `CompanyStore` in-process. `ReleaseLocator` opens `Kbo:Database` (a .db path, used in dev and tests) or `Kbo:Share` (a directory with `current.txt`, used in Azure). |
| `Net.Code.Kbo.Tests` | TUnit tests for the projects above: fixture zip (`KboFixture`), plus web tests through `WebApplicationFactory` (`WebApp`). |
| `kbo.AppHost`, `kbo.ServiceDefaults` | Aspire: the AppHost runs `Net.Code.Kbo.Web`; phase 3 adds the Azure resources. |
| `specs/azure-architecture.md` | **The plan**: decisions, schema, document shape, release flow, API/UI design, measurements, open questions. |
| *Legacy, deleted in phase 4:* `Net.Code.Kbo.Data`, `Net.Code.Kbo.Cli`, `Net.Code.Kbo.Api`, `Net.Code.Kbo.Data.Tests`, the other `specs/*` | The EF Core / incremental-import implementation. **Don't extend it.** |

## Build, run, test

```sh
dotnet build kbo.sln
dotnet test                                     # all test projects (global.json opts into Microsoft.Testing.Platform)
dotnet run --project Net.Code.Kbo.Tests         # new tests only
dotnet run --project Net.Code.Kbo.Tests -- --treenode-filter "/*/*/SearchQueryTests/*"

# release database from a KBO Full zip (about 7.5 min and 3.2 GB RAM for the real extract)
dotnet run -c Release --project Net.Code.Kbo.Build -- build -i KboOpenData_0502_..._Full.zip -o data/kbo-0502.db
dotnet run -c Release --project Net.Code.Kbo.Build -- search -d data/kbo-0502.db colruyt halle
dotnet run -c Release --project Net.Code.Kbo.Build -- get -d data/kbo-0502.db 0403.170.701
dotnet run -c Release --project Net.Code.Kbo.Build -- release --share <dir> [--work <dir>]

# web app on http://localhost:5280, serving ../data/kbo-0502.db (appsettings.Development.json)
dotnet run --project Net.Code.Kbo.Web            # or: dotnet run --project kbo.AppHost
```

- `data/` is gitignored. Never commit `.db` files or KBO data.
- TUnit assertions are async: `await Assert.That(x).IsEqualTo(y);`.
- `KboFixture` is a tiny KBO-shaped zip with the edge cases that matter: units, quoted names with commas and doubled quotes, a struck-off address, a FAX contact, several NACE versions, an orphan row. When you change parsing, the document shape or search, extend it and `ReleaseBuilderTests`.
- `BuiltRelease` opens its store with `commonTermFraction: 0.5`. In a 4-company fixture, the 10% default would treat every word as common.
- Web tests use `WebApplicationFactory<Net.Code.Kbo.Web.Components.App>`. `Program` can't be the marker, because the `kbo` tool also has a top-level `Program` and the test project sees both.
- For performance-sensitive changes, build the real zip and compare against the measurements in §11 of the plan.

## Key design points (details in the plan)

- **Build (`ExtractModel`):** loads the whole extract into memory (about 3.2 GB), streaming CSVs straight from the zip with Net.Code.Csv. It then writes one row per enterprise, in number order, to `company` (summary + document JSON) and `company_fts`. Establishments and branches are nested in their enterprise's document.
- **Documents:** they store codes, not descriptions; `CodeBook` resolves descriptions per language (falling back to NL, then FR, then any language). Empty values are omitted from the JSON.
- **Activities:** a document keeps only the newest NACE version per (activity group, classification). The search index holds the descriptions of all versions.
- **Skipped or kept data:**
  - Struck-off addresses stay in the document with `strikingOff`, but aren't searchable.
  - FAX contacts are skipped, because `code.csv` has no FAX code.
  - Orphan rows are counted (`orphanEntities`).
- **Search index:** `company_fts` is contentless and its rowid is the enterprise number. It has **no prefix index**, a deliberate choice: one would add 1.8 GB. `company_fts_vocab` (fts5vocab) gives per-word document counts.
- **Search queries (`CompanyStore.Search`):**
  - Sort and limit inside the FTS subquery, then join `company`; joining first is 3–4× slower.
  - Free-text words matching more than 10% of companies are dropped. If nothing else remains, results come back unranked.
- **Opening a release:** `CompanyStore` opens the file as `file:<path>?immutable=1` read-only, with one pooled connection per call. A published release file must never change in place.
- **Releasing (`Releaser`):** takes the newest `incoming/*_Full.zip` newer than `current.txt`, builds and validates it, copies it to the share, and writes `current.txt` last. It keeps the current and previous release and moves the zip to `incoming/processed/`. Validation errors mean nothing is published (exit code 1).

## Web app notes

- **Language:** `?lang=nl|fr` in the query string; anything else falls back to nl. `code.csv` only has NL and FR descriptions. `CompanyView`/`SummaryView` (`Net.Code.Kbo/Views.cs`) resolve codes for both the API and the UI.
- **Not found:**
  - Pages call `NavigationManager.NotFound()`, which renders `Pages/NotFound.razor` with status 404.
  - Unknown non-API URLs are re-executed to `/not-found`. This needs the explicit `UseRouting()` after the status-code middleware in `Program.cs`.
  - API errors are problem details.
  - Don't set `Response.StatusCode` by hand in components: the body comes out empty.
- **Encoding:** HTML is written with all Unicode ranges allowed (`AddWebEncoders`), so accented names aren't turned into `&#xE9;`.
- **Caching:** API responses carry `ETag: "<extract>-<builtAt>"` and `Cache-Control: public, max-age=3600`; `If-None-Match` gives 304.

## Conventions

- Target framework `net11.0` everywhere, with nullable and implicit usings. Microsoft packages are on `11.0.0-rc.*` until .NET 11 GA; bump them then.
- Prefer records, primary constructors, `switch` expressions and list patterns. Keep the code plain: simplicity is a stated priority.
- Commit directly on `main`.
- `specs/azure-architecture.md` is the source of truth for the design. Update it when you change a decision, and record measurements in §11.
