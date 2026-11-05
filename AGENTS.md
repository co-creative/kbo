# AGENTS.md

Guidance for AI coding agents working in this repository.

## What this is

A .NET 11 solution that imports the Belgian **KBO open data** (Kruispuntbank van Ondernemingen, the crossroads bank for enterprises) from its CSV dump into a **SQLite** database and serves company lookups through a small HTTP API.

The official data-model description is `Net.Code.Kbo.Data/Cookbook-KBO-Open-Data.pdf`. The data itself is not in the repo: you download it from kbopub.economie.fgov.be after creating an account.

## Solution layout

| Project | Role |
|---|---|
| `Net.Code.Kbo.Data` | Core library: EF Core model, CSV import pipeline, company query service, `KboNr` type. All real logic lives here. |
| `Net.Code.Kbo.Cli` | Console tool (assembly name `kbo`): verbs `import` (default), `init`, `report`, `rebuild-cache`, `test-progress`. Progress UI uses Spectre.Console. |
| `Net.Code.Kbo.Api` | ASP.NET Core minimal API (read-only). Has a Dockerfile. |
| `kbo.AppHost` | .NET Aspire host (Aspire 13, SDK-style `Sdk="Aspire.AppHost.Sdk/13.x"`); it only runs the API. |
| `Net.Code.Kbo.Tests` | TUnit tests (Microsoft.Testing.Platform): unit tests plus an end-to-end CSV → import → query test. |
| `kbo.ServiceDefaults` | Standard Aspire service defaults (OpenTelemetry, health checks, resilience). |
| `specs/` | Design notes: pipeline overview, CompanyDocuments JSON cache, FTS5 plan, CLI progress, reference `schema.sql`. Read the relevant spec before changing import/search behaviour. |

## Build and run

```sh
dotnet build kbo.sln

# Import (CSV folder from KBO, unzipped). Full import requires an empty DB.
dotnet run --project Net.Code.Kbo.Cli -- import -i <csv-folder> -d data.db
dotnet run --project Net.Code.Kbo.Cli -- import -i <csv-folder> -d data.db --incremental
dotnet run --project Net.Code.Kbo.Cli -- import -i <csv-folder> -d data.db -f enterprise -f address   # selected files only
dotnet run --project Net.Code.Kbo.Cli -- rebuild-cache -d data.db [--docs | --fts]

# API (connection string "ConnectionStrings:Kbo", default Data Source=..\data\data.db)
dotnet run --project Net.Code.Kbo.Api
# or via Aspire
dotnet run --project kbo.AppHost
```

### Tests

```sh
dotnet test                                    # all tests (global.json opts dotnet test into Microsoft.Testing.Platform)
dotnet run --project Net.Code.Kbo.Tests        # same, runs the TUnit executable directly
dotnet run --project Net.Code.Kbo.Tests -- --treenode-filter "/*/*/KboNrTests/*"   # filter
```

- TUnit assertions are async: `await Assert.That(x).IsEqualTo(y);`. The analyzer flags assertions that aren't awaited.
- `ImportedDatabase` (a shared class fixture) writes a tiny KBO-shaped CSV set to a temp folder and runs `FullImport` into a temp SQLite file, building the relational tables, CompanyDocuments and FTS. Tests then query through `ICompanyService`. When you change the import, the CompanyDocuments/FTS SQL or the query service, extend that fixture and `ImportAndQueryTests`.
- Fixture code values must match what the service hardcodes: language `1` = FR and `2` = NL; denomination type `001` = name and `003` = commercial name.
- The Data project exposes internals to the test project (`InternalsVisibleTo`), e.g. `CompanyService.BuildFtsMatchExpression`.
- For performance-sensitive changes, also run the CLI against a real KBO dump. The CLI's `--limit` option is parsed but not wired into the import. `Net.Code.Kbo.Api/Net.Code.Kbo.Api.http` has sample requests.
- Do not commit `.db` files or KBO CSV data.

## Architecture

### Data access: two styles side by side
- **EF Core** (`KboModel/KboDataContext.cs`) defines the schema. The DB is created with `Database.EnsureCreated()`; there are **no migrations**. Schema changes to the model take effect only on a freshly created database.
- **Net.Code.ADONet** (`IDb`, raw SQL) is used for bulk import, CompanyDocuments, FTS, and free-text search. The import uses the `System.Data.SQLite` provider (for its progress/commit events); the API and EF use `Microsoft.Data.Sqlite`. Keep that split unless you have a reason to change it.

### Model notes
- All code tables (`JuridicalForm`, `TypeOfAddress`, `Nace2008`, ...) are subclasses of `Code` (TPH, one `Codes` table) with multilingual `CodeDescription`s. Descriptions are resolved by language at query time (`NL`, `FR`, sometimes `DE`/`EN`; default `EN`).
- `KboNr` (`Service/KboNr.cs`) is a `record struct` around a `long`. It parses any string containing exactly 10 digits. It is stored as `"F"` format (`0123.456.789`) via an EF value conversion, and it binds directly as a route parameter through its `TypeConverter`. Use it, not raw strings, for enterprise numbers.
- Establishments and branches belong to an enterprise. Addresses, denominations, contacts and activities are keyed by *entity number* (enterprise, establishment or branch id), so company queries collect all entity numbers first.

### Import pipeline (`Import/`)
- `ImportService` builds a `Pipeline` of `PipelineStep`s: Meta → Codes → Enterprises → Establishments → Branches → Addresses → Denominations → Contacts → Activities → `RebuildCompanyDocuments` → `RebuildFtsIndex`.
- `ImportModel.cs` holds the CSV row types (read with Net.Code.Csv). `Tables.cs` holds the DB row types plus a static `MapFrom` for each that returns a `MapResult`; failed maps are counted as errors and skipped. `CodeCache` resolves code values to ids.
- **Full import** drops and recreates each table. `DropAndRecreate` reuses the `CREATE TABLE` SQL from `sqlite_master` and returns the table's `CREATE INDEX` statements, which `ImportFull` runs *after* the bulk insert. Indexes defined in the EF model therefore survive an import; `FullImport_KeepsModelIndexes` guards this. A missing CSV skips that table. A full import refuses to run on a non-empty DB unless a debugger is attached.
- **Incremental import** reads `<name>_delete.csv` and `<name>_insert.csv` next to the base path and applies delete-then-insert by key. Delete keys come from the first CSV column with the quotes stripped. `ImportFiles` (CLI `-f`) accepts `address` or `address.csv`. It does **not** rebuild CompanyDocuments/FTS, so run `rebuild-cache` afterwards.
- Lookups by `EntityNumber` (Denominations, Contacts, Activities) and by `EnterpriseNumber` (Establishments, Branches) depend on indexes. Without them every company lookup scans tens of millions of rows. Keep them in the model.
- KBO's `code.csv` has no `FAX` contact type, so the about 19k FAX contacts are rejected as mapping errors. That's expected.
- Progress goes through `IPipelineReporter` (the CLI implements it in `SpectreTaskProgressReporter`). Steps provide row estimates through `GetEstimate`.
- Cancellation (Ctrl+C) flows through a `CancellationToken`. Keep new steps cancellable.

### Derived data
- The CompanyDocuments/FTS aggregation maps every entity number (enterprise, establishment, branch) to its enterprise, then `group_concat(DISTINCT …)`s the addresses and activities. Establishment streets and cities are therefore searchable.
- `CompanyDocuments`: one JSON payload per enterprise (with ETag, JsonVersion and UpdatedAt), rebuilt by the pipeline. See `specs/company-json-cache.md`.
- `companies_locations_fts`: a contentless FTS5 table (`unicode61 remove_diacritics 2`, prefix `2 3 4`) whose rowids line up with `CompanyDocuments.rowid`. See `specs/fts5-plan.md`. If you change one of these, rebuild both with `rebuild-cache`.

### API (`Net.Code.Kbo.Api/Program.cs`) → `ICompanyService`
- `GET /companies/{id}?language=`: one company. Currently built with an EF query plus several follow-up queries, not yet from CompanyDocuments.
- `GET /companies?name=&street=&houseNumber=&postalCode=&city=&language=`: structured search with LinqKit predicates, at most 25 results.
- `GET /companies/search?text=&language=&skip=&take=`: free-text FTS5 search ranked by weighted `bm25` (name > city > postal code > street > activity). Returns 204 when nothing matches. `take` is capped at 25.

## Conventions
- Target framework is `net11.0` everywhere. Nullable reference types and implicit usings are on everywhere. The code uses C# 14 features such as `extension(...)` blocks (`Import/Extensions.cs`) and the `field` keyword (`CodeCache`).
- Package versions: the Microsoft.Extensions, EF Core, Data.Sqlite and AspNetCore.OpenApi packages are on `11.0.0-rc.*` because .NET 11 is not GA yet. Bump them to the GA versions when it ships. Spectre.Console is deliberately kept on its latest stable release, not its alpha builds.
- Prefer primary constructors, records, `switch` expressions and list patterns, matching the existing code.
- Register services through the `AddCompanyService` / `AddImportService` extension methods in `Net.Code.Kbo.Data/Extensions.cs`.
- Most types in the Data project are `internal`. Only `ICompanyService`, the DTOs, `KboNr`, the EF model and the DI extensions are public.
- The specs in `specs/` describe intent and can be ahead of or behind the code. Check the code, and update the spec when you change the design.
