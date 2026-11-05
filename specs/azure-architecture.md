# KBO on Azure: architecture and rewrite plan

Status: proposed (2026-11-05)
Supersedes: `pipeline-overview.md`, `company-json-cache.md`, `fts5-plan.md`, `cli-import-progress.md` (removed in phase 4)

## 1. Context and goals

- **Data:** the KBO Open Data "Full" extract, published monthly as a zip of about 300 MB (2.2 GB of CSV). Measured on extract 0502 (2026-10-04):

  | File | Rows |
  |---|---|
  | enterprise | 1,970,592 |
  | establishment | 1,703,983 |
  | branch | 7,321 |
  | address | 2,906,215 |
  | denomination | 3,380,544 |
  | contact | 715,482 (19,428 `FAX` rows have no code in `code.csv`) |
  | activity | 34,412,630 |

  The largest enterprise, bpost, has 1,060 establishments. 33 enterprises have 200 or more, and 240 have between 50 and 199.
- **Usage:** read-only lookups and search. There's one user for now (low traffic), with possible commercial use later.
- **Priorities:** simplicity first, then low running cost. Backward compatibility isn't required.
- **Freshness:** the monthly update file contains exactly the changes between two consecutive full files (KBO cookbook §1.5.2), so rebuilding monthly from the full file loses no freshness. **There is no incremental import.**

## 2. Decisions

| # | Decision | Why |
|---|---|---|
| D1 | Each monthly extract becomes **one immutable, read-only SQLite file** (`kbo-<extract>.db`) | No database server, no migrations, no write paths. A release is just a file, and rollback means pointing back at the previous file. |
| D2 | The API and UI open that file **directly from an Azure Files share** (read-only, `immutable=1`) | Nothing is downloaded at startup, so the app can scale to zero, and there's no per-replica disk limit. Network latency is fine at this traffic level (to be measured, §11). |
| D3 | **One web app** hosts both the JSON API (`/api/...`) and the Blazor UI | One image, one Container App, one deployment. The UI calls the query layer in-process, not over HTTP. |
| D4 | The Blazor UI uses **static server-side rendering** (no interactive mode) | No SignalR circuits or WebAssembly payload. Works with scale-to-zero, plain GET forms give shareable URLs, and pages work without JavaScript. |
| D5 | **Full rebuild every month** in a Container Apps job | Removes the incremental path and its whole class of bugs. |
| D6 | The build is **streaming C#**: CSVs are read straight from the zip and turned into documents in memory, then written in one pass | No staging database, no large SQL CTEs (the last approach exhausted RAM), and plain code that's easy to unit test. |
| D7 | Documents store **codes, not descriptions**; descriptions come from a small `code` table loaded into memory | Avoids repeating 4-language NACE text in 2M documents. Languages are resolved per request. |
| D8 | Search is **SQLite FTS5**. Typo tolerance comes later via `fts5vocab` plus edit distance (§7.3) | No extra service. Azure AI Search stays an upgrade path (§12). |
| D9 | The Aspire AppHost describes the infrastructure; deploy with `azd up` | Reuses the existing AppHost, and the same model runs locally. |

## 3. Architecture

```
            you (monthly)                      ┌─────────────── Azure (West Europe) ────────────────┐
  KBO portal ──download zip──► upload ──────►  │ Storage account                                     │
                                               │   Azure Files share "kbo"                           │
                                               │     incoming/KboOpenData_0502_..._Full.zip          │
                                               │     kbo-0502.db   kbo-0501.db   current.txt         │
                                               │          ▲ write (rw mount)       ▲ read (ro mount) │
                                               │          │                        │                 │
                                               │ Container Apps environment (Consumption only)       │
                                               │   Job  "kbo-build"  cron daily, 4 vCPU / 8 GiB      │
                                               │   App  "kbo-web"    0.5 vCPU / 1 GiB, min 0         │
                                               │          ├─ /            Blazor (static SSR)        │
                                               │          ├─ /api/...     minimal API                │
                                               │          └─ /health      ServiceDefaults            │
                                               │                                                     │
                                               │ Container Registry (Basic) · Log Analytics + App Insights │
                                               └─────────────────────────────────────────────────────┘
```

## 4. Solution layout (after the rewrite)

| Project | Contents |
|---|---|
| `Net.Code.Kbo` (class library) | `KboNr`; the document model (records); `CodeBook` (in-memory code descriptions); `CompanyStore` (read-only SQLite queries: get, search, release); `SearchQuery` (user input → FTS5 MATCH) |
| `Net.Code.Kbo.Build` (console, assembly `kbo`) | `kbo build -i <zip> -o <db>` locally; `kbo release --share <path>` in the job (find new zip → build → validate → publish) |
| `Net.Code.Kbo.Web` | Minimal API + Blazor components (static SSR), referencing `Net.Code.Kbo` |
| `Net.Code.Kbo.Tests` | TUnit: build tests (fixture zip → db), store/search tests, web tests through `WebApplicationFactory` |
| `kbo.AppHost`, `kbo.ServiceDefaults` | Aspire: resources, local orchestration, `azd` deployment |

**Removed:** `Net.Code.Kbo.Data` (EF Core, LinqKit, the import pipeline and progress reporting), `Net.Code.Kbo.Api` and `Net.Code.Kbo.Cli`. **Dependencies:** `Microsoft.Data.Sqlite`, `Net.Code.Csv`, and optionally `Net.Code.ADONet` for the build's bulk inserts. No EF Core, LinqKit or System.Data.SQLite.

## 5. Serving database (`kbo-<extract>.db`)

```sql
CREATE TABLE release (            -- exactly one row
  extract_number INTEGER NOT NULL,
  snapshot_date  TEXT    NOT NULL,  -- from meta.csv
  built_at       TEXT    NOT NULL,  -- ISO-8601 UTC
  counts         TEXT    NOT NULL   -- JSON: rows per source file + rejected rows
);

CREATE TABLE code (                 -- ~21k rows; loaded into memory by the web app
  category TEXT NOT NULL, code TEXT NOT NULL, lang TEXT NOT NULL, description TEXT NOT NULL,
  PRIMARY KEY (category, code, lang)
) WITHOUT ROWID;

CREATE TABLE company (
  number  INTEGER PRIMARY KEY,      -- KboNr.Value (0403.170.701 → 403170701) = rowid
  summary TEXT NOT NULL,            -- small JSON for result lists
  doc     TEXT NOT NULL             -- full JSON document (codes only)
);

CREATE VIRTUAL TABLE company_fts USING fts5(
  name, street, city, postal_code, activity,
  content = '',                     -- contentless: rowid = company.number
  tokenize = 'unicode61 remove_diacritics 2'
  -- no prefix index: it adds 1.8 GB and 40% build time for ~0.1 s on broad prefixes (§11)
);
```

- **Search rows:** `company_fts.rowid = company.number`, so search hits join straight to companies. There's no mapping table and no reliance on insertion order.
- **What goes into the search index:**
  - `name`: all names of the enterprise, its establishments and branches.
  - `street` and `city`: NL and FR values, de-duplicated.
  - `postal_code`: all postal codes.
  - `activity`: NL and FR descriptions of the distinct NACE codes, **all versions** (2003/2008/2025), de-duplicated.
- **Finishing the build:** after loading, run `INSERT INTO company_fts(company_fts) VALUES('optimize')`, `ANALYZE` and `PRAGMA journal_mode=DELETE`. The file is then self-contained (no `-wal` file) and its search index segments are merged.

### Document (`company.doc`), language-neutral

```json
{
  "number": "0403.170.701", "status": "AC", "startDate": "2001-01-01",
  "typeOfEnterprise": "2", "juridicalSituation": "000", "juridicalForm": "014", "juridicalFormCac": null,
  "names":      [{ "type": "001", "lang": "2", "value": "Acme Software" }],
  "addresses":  [{ "type": "REGO", "zip": "2000", "city": { "nl": "Antwerpen", "fr": "Anvers" },
                   "street": { "nl": "Meir", "fr": "Meir" }, "number": "1", "box": "", "extra": "" }],
  "contacts":   [{ "type": "EMAIL", "value": "info@acme.example" }],
  "activities": [{ "group": "001", "nace": "2008/62010", "classification": "MAIN" }],
  "establishments": [{ "number": "2.123.456.789", "startDate": "2005-01-01",
                       "names": [], "addresses": [], "contacts": [], "activities": [] }],
  "branches": [ ... same shape ... ]
}
```

Empty values are omitted from the JSON.

`summary`: `{ "number", "name", "zip", "city": {nl, fr}, "juridicalForm", "status", "establishments": <count> }`.

**NACE versions (decided, Q3):** KBO lists every activity once per NACE-BEL revision (2003, 2008, 2025). Of the 34,412,630 rows, 17.1M are 2025, 15.1M are 2008 and 2.2M are 2003. For example, `0403.170.701`'s main VAT activity appears three times: 2003 `40110`, 2008 `35110`, 2025 `35110`.
- **Document:** per (entity, activity group, classification), keep only the **newest version present**. In practice that's 2025, falling back to 2008 or 2003 for the roughly 25k groups never recoded. This keeps 17,165,798 rows (−50%), and history is dropped from the document.
- **Search index:** the `activity` column holds the descriptions of **all** versions, so searches on older wording still match.

## 6. Build and release (`Net.Code.Kbo.Build`)

1. **Find work.** Look for the newest `incoming/*_Full.zip` whose extract number is greater than the one in `current.txt`. If there isn't one, exit 0 (the daily cron is a no-op on most days).
2. **Load into memory,** reading CSV entries straight from the zip (no extraction):
   - load `code` and `meta`;
   - load enterprises, establishments, branches, denominations, addresses and contacts into dictionaries keyed by entity number;
   - stream `activity.csv`. Per (entity, group, classification), keep the codes of the newest NACE version for the document; collect the distinct NACE codes of all versions per owning enterprise for the search index.
3. **Write** `kbo-<extract>.db` on local disk: insert companies in `number` order, plus the FTS rows (one transaction, prepared statements), then run the §5 finishing steps.
4. **Validate before publishing.** The build fails, and nothing is published, if any of these fail:
   - row counts per source file;
   - the rejected-row rate stays under a threshold (FAX contacts are known and expected);
   - smoke queries, such as known numbers like bpost `0214.596.464` and searches like `colruyt halle`;
   - file size is under 6 GB (the job has 8 GiB of disk; 0502 is 4.46 GB).
5. **Publish:**
   - copy the file to the share as `kbo-<extract>.db.tmp`, then rename it to `kbo-<extract>.db`;
   - write `current.txt` last;
   - delete releases older than the previous one;
   - move the zip to `incoming/processed/`.
6. **Rollback** means editing `current.txt`. The web app reads it at startup, so it takes effect on the next cold start, or immediately via a revision restart.

**Job size:** 4 vCPU / 8 GiB (consumption maximum, 8 GiB ephemeral disk). Peak RAM is estimated at 3–5 GB and **must be measured in the spike**. If it doesn't fit, sort-merge per entity number instead of holding everything in memory.

## 7. Web app (`Net.Code.Kbo.Web`)

### 7.1 Query layer: `CompanyStore` (singleton)

- **At startup:** read `current.txt` and open `Data Source=file:<share>/kbo-<n>.db?immutable=1;Mode=ReadOnly` (URI support to be confirmed in the spike). Then load `code` into a `CodeBook` and read `release`.
- **`Get(KboNr)`:** one primary-key read. The document is projected to the requested language through `CodeBook` (fallback order: requested → NL → FR → any).
- **`Search(SearchQuery, skip, take)`:** a `MATCH` query ordered by `bm25(company_fts, 5, 1, 4, 2.5, 0.5)` (name, street, city, postal code, activity), returning summaries. `take` is capped at 25. Sort and limit inside the FTS subquery, then join `company` for only those rows (3–4× faster, §11).
- **Very common terms:** a term is common when its index entries (prefix-expanded, summed from `company_fts_vocab`) cover more than 10% of companies. Common free-text terms are dropped when other terms remain. Otherwise, common terms, including field filters, are matched exactly without a wildcard and returned unranked (`LIMIT` without `ORDER BY`) with a "too broad" hint. Without a prefix index, expanding `de*`, `ge*` or `ver*` costs 0.5–2.5 s, while exact `"de"` costs 0.2 ms (§11).
- **Turning input into a search query (`SearchQuery`):**
  - Free text is tokenized like today (letters; 4-digit numbers kept, other digit runs dropped; at most 12 tokens). Each token is emitted as a quoted string, which neutralizes FTS5 syntax; letter tokens get `*` for prefix matching.
  - Structured parameters become column filters, e.g. `city:"gent"*` or `postal_code:"9000"`.

### 7.2 JSON API

| Route | Result |
|---|---|
| `GET /api/companies/{number}?lang=nl` | 200 company (language-projected) / 404 |
| `GET /api/companies?q=&name=&street=&city=&postalCode=&lang=&skip=&take=` | 200 `{ total?, items: [summary…] }` (always 200, possibly empty) |
| `GET /api/release` | extract number, snapshot date, built at, counts |

- **Headers:** responses carry `ETag: "<extract>"` and `Cache-Control: public, max-age=3600`.
- **Errors:** RFC 7807 problem details.
- **Docs:** an OpenAPI document is served at `/openapi/v1.json`.

### 7.3 Typo tolerance (phase 5, optional)

If a word gives no hits, look it up in `fts5vocab(company_fts, 'row')`: candidate words share the first 2 characters, and edit distance is at most 1 (2 for words of 8 or more characters). Retry with `(word OR candidate…)`. That's about 50 lines in `SearchQuery`, with no new service and no index change.

### 7.4 Blazor UI (static SSR, no interactivity)

| Page | Content |
|---|---|
| `/` and `/search?q=…&city=…&postalCode=…&lang=…&page=…` | One search box plus an "advanced" fold-out (name, street, city, postal code). Uses a GET form, so every search is a shareable URL. The result list shows name, number, city, juridical form and establishment count, with paging. |
| `/companies/{number}` | Header with name, number, status, juridical form and start date. Below: main address (with a map link), contacts, activities (main activity first), and establishments as a table showing the first 50 with a "show all" link (bpost has 1,060). Then branches, and a link to the JSON (`/api/companies/{number}`). |
| Layout | NL/FR language switch (the `lang` query value is kept on links; `code.csv` has no DE/EN descriptions), plus a footer with the extract number and snapshot date. |

- **Rendering:** components with `[StreamRendering]` where useful. No interactive render modes, no component library, and one small CSS file.
- **Shared code:** the UI injects `CompanyStore` directly, so the API and UI use the same query code.
- **Later:** typeahead (an `InteractiveServer` island or a few lines of JS against `/api/companies?q=`) only if it turns out to be missed.

### 7.5 Access

For now, turn on **Container Apps built-in authentication (Entra ID)** for the whole app, allowing only you. That needs no code. Opening the API publicly later is a configuration change, subject to Q1.

## 8. Azure resources (modelled in `kbo.AppHost`, deployed with `azd up`)

| Resource | Settings |
|---|---|
| Resource group | `rg-kbo`, West Europe |
| Storage account | Standard LRS, hot; Azure Files share `kbo` (about 10 GB quota) |
| Container Apps environment | Consumption only (no dedicated workload profile, so no fixed management fee); share registered as SMB storage. SMB mounts authenticate with the account key, which is stored as an environment secret. |
| Container App `kbo-web` | 0.5 vCPU / 1 GiB, min replicas 0, max 2; share mounted read-only; built-in auth on; HTTP ingress |
| Container Apps Job `kbo-build` | schedule trigger (daily 06:00), 4 vCPU / 8 GiB, replica timeout 2 h; share mounted read-write; also startable on demand (`az containerapp job start`) |
| Container Registry | Basic |
| Log Analytics + Application Insights | OpenTelemetry via ServiceDefaults |

Aspire 13 can publish a project as a Container Apps job and add Azure Files volumes. **Verify this in phase 3.** If it falls short, add a small Bicep module through `azd`.

## 9. Cost (West Europe retail prices, EUR/month, from the Azure Retail Prices API on 2026-11-05)

| Item | Unit price | Monthly |
|---|---|---|
| `kbo-web`, scale-to-zero, one user | active vCPU $0.000034/s, memory $0.000004/GiB-s | ≈ €0 (inside the free grant) |
| `kbo-build`, about 30 min/month at 4 vCPU / 8 GiB | same | < €1 |
| Azure Files, about 6 GB | €0.0243/GB; reads €0.005 per 10K | ≈ €0.20 |
| Container Registry Basic | €0.1496/day | €4.60 |
| Log Analytics | first 5 GB free, then €2.68/GB | €0 |
| **Total** | | **≈ €5** |

**Reference points when traffic grows:**
- An always-on replica with a local copy of the database (1.25 vCPU / 2.5 GiB, needed for 8 GiB of disk) costs €35 idle to €124 fully busy.
- Azure AI Search Basic costs €66 per unit (€132 for 2 units, which the query SLA requires).

## 10. Phases

| Phase | Deliverable | Done when |
|---|---|---|
| **0. Spike** (about 1 day) ✅ | Quick `kbo build` on the real 0502 zip | File size, build time, peak RAM, and query latency for get and search (local disk vs. Azure Files mount) are measured and written into §11. The design is confirmed or adjusted. |
| **1. Core + Build** ✅ | `Net.Code.Kbo`, `Net.Code.Kbo.Build`, tests on a fixture zip | `kbo build` produces a valid db from the real zip, validation passes, and tests are green |
| **2. Web** ✅ | `Net.Code.Kbo.Web` (API + Blazor SSR), tests via `WebApplicationFactory` | Local `dotnet run` (or the AppHost) serves search and detail pages against a built db; tests are green |
| **3. Azure** | AppHost resources, `azd up`, first release published by the job | The UI works on Azure behind Entra auth; a job run publishes `kbo-0502.db`; rollback has been tested |
| **4. Cleanup** | Delete `Net.Code.Kbo.Data`, `.Api`, `.Cli` and the superseded specs; update AGENTS.md and README | The solution builds, tests are green, and no EF Core is left |
| **5. Optional** | Typo tolerance (§7.3), a typeahead island, an automated KBO download (only if the terms allow) | as needed |

## 11. Measurements

Spike on 2026-11-05: a single-file streaming build of extract 0502 straight from the zip. Laptop, local SSD, warm OS cache unless noted.

| Metric | Estimate | Measured (local disk) |
|---|---|---|
| `kbo-<n>.db` size | 2–3 GB | **4.46 GB** (chosen variant: no prefix index, empty JSON values omitted). Documents: 2.18 GB, about 1.2 KB on average; the largest is 666 KB (`0446.891.668`), bpost 497 KB. With `prefix='2 3'` the file was 6.25 GB. |
| Build time / peak RAM | about 10 min / 3–5 GB | **7.4 min / 3.2 GB** (12 min with the prefix index). Loading the zip takes 47 s. |
| `Get` latency | < 1 ms | **0.016 ms** random, 0.9 ms for bpost |
| `Search` specific ("colruyt halle", "bakkerij 9000", "city:gent name:bakkerij") | about 5 ms | **0.5–11 ms** warm, 4–14 ms cold |
| `Search` common prefix ("gent*" 58k hits, "antw*" 103k, "bv*" 57k) | ? | **100–290 ms** (83–112 ms with the prefix index). Joining `company` before sorting made it 3–4× slower. |
| `Search` stopword ("de", 1.66M of 1.97M companies) | ? | **1.5–4.8 s** as a prefix; handled by the common-term rule (§7.1): exact `"de"` takes 0.2 ms and the frequency check 80–190 ms |
| **Phase 1 `kbo build` (Net.Code.Csv, real zip)** | | **4.9 min, 3.45 GB peak RAM, 4.31 GB file**; validation: 183/190 sampled companies found by name |
| **Phase 1 CLI search incl. process start** | | `colruyt halle` 0.23 s, `gent` 0.39 s, `de` / `ge` / `van de` 0.4–0.7 s |
| Read-only URI `file:…?immutable=1` with Microsoft.Data.Sqlite (Q4) | ? | **works** |
| Azure Files latency, cold start | ? | not measurable from a laptop; measure in phase 3 |

Consequences for the design (applied in §5–§7):
- **No prefix index:** it costs 1.8 GB and 40% of build time to save about 0.1 s on broad one-word prefixes.
- **Search query shape:** sort and limit inside the FTS subquery, then join `company`.
- **Very common words:** the common-word rule in `SearchQuery`.
- **No document compression for now:** 4.46 GB fits the job's 8 GiB of disk, and validation fails above 6 GB. If the local-copy upgrade path (§12) is ever needed, compressing `doc` (about 2.2 GB of JSON) is the lever.

## 12. Risks, open questions, upgrade paths

- **Q1, licence:** check the KBO Open Data terms for redistributing the data through a public API or UI. This decides when built-in auth can be turned off.
- **Q2, SMB latency:** Azure Files latency for search on common words. Fallback: copy the db to local disk at startup and keep minimum replicas at 1 (≈ €35+/month).
- **Q3, NACE versions (decided 2026-11-05):** the document keeps the newest version per (entity, group, classification); the search index keeps the descriptions of all versions. See §5.
- **Q4, URI filenames (resolved):** Microsoft.Data.Sqlite accepts `Data Source=file:<path>?immutable=1;Mode=ReadOnly` (spike).
- **Q5, FAX contacts (decided 2026-11-05):** `code.csv` has no `FAX` contact type, so the 19,428 FAX rows are skipped. Validation counts them as expected rejections.
- **Upgrade path, more traffic:** set min replicas to 1 or more and use a local db copy.
- **Upgrade path, better search:** Azure AI Search. The job also pushes flattened search fields to a new index per release and flips an index alias (aliases are GA in API version 2026-04-01). Full documents stay where they are, in the SQLite file or in Blob. Don't use Table Storage: bpost's document is larger than Table's 64 KiB property limit.
