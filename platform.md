# kbo on the co-creative platform

Belgian enterprise (KBO Open Data) lookup: API and web UI

- **URL:** https://kbo.co-creative.be (health check: `/health`, always anonymous)
- **Azure resources:** resource group `rg-kbo` (Sweden Central), shared Container Apps environment and registry `crcocreative.azurecr.io`
- **What this repository owns** (applied by every push to `main`):
  - `platform.app.json`: settings (`env`), CPU and memory, replicas, job schedules and timeouts;
  - `infra/app.bicep` (optional): the app's own Azure resources in `rg-kbo` (storage and file shares, a free SQL
    database, Service Bus queues, SignalR), from the platform's registry module `bicep/app-resources`;
  - `platform.images.json`: the images to build.
- **What the platform owns** (`apps/kbo/main.bicepparam` in co-creative/platform): which components exist
  (the web app, job names and their trigger type), sign-in, hostname and DNS, file-share mounts, AI, and the
  **ceilings** (maximum CPU, memory and replicas). Ask there for a new job, a mount or a higher ceiling.

> This file, `.github/workflows/platform-deploy.yml` and `.github/platform/*` are owned by the platform: updates
> arrive as pull requests. You own everything else.

## Delivery

Every push to `main` runs `.github/workflows/platform-deploy.yml`:

1. `dotnet test` (pull requests run this step only)
2. deploys `infra/app.bicep` (if present) and validates `platform.app.json` against the components and ceilings
3. builds each image in `platform.images.json`, runs the **contract check** (each web image must start on the runner
   without Azure and answer `/health` with 200; opt out per image with `"smoke": false`), then pushes it to
   `crcocreative.azurecr.io/kbo/<name>:<commit sha>`
4. updates the web app (`kbo-web`) and jobs (`kbo-<job>`): new image plus the configuration from
   `platform.app.json` and the settings of your own resources, one revision each
5. records the delivery on the resource group (tags `lastDeliverySha`, `lastDeliveryAt`)
6. smoke-tests `https://kbo.co-creative.be/health`

The CI identity can push images, update this app's container apps and jobs, and manage resources inside
`rg-kbo` within the platform's policies (allowed resource types, free SQL only, no storage keys). It can't create
web apps or jobs, change identities, or touch anything outside `rg-kbo`.

### platform.app.json

```json
{
  "$schema": "./.github/platform/platform.app.schema.json",
  "web": { "cpu": "0.25", "memory": "0.5Gi", "minReplicas": 0, "maxReplicas": 1, "env": { "Feature__X": "on" } },
  "jobs": { "nightly": { "cpu": "0.5", "memory": "1Gi", "schedule": "0 3 * * *", "replicaTimeout": 1800, "env": {} } }
}
```

- Settings you remove from `env` are removed from the component on the next delivery (only settings this file
  declared before; platform settings are never touched). Platform settings (`AZURE_CLIENT_ID`, `APP_SLUG`,
  `PLATFORM_*`, …) can't be set here.
- Memory is 2 GiB per vCPU. Values above the ceilings fail the delivery.
- Changing a setting here **is a production change**: it applies on the next push to `main`.

### platform.images.json

```json
[
  { "name": "web", "dockerfile": "Dockerfile", "context": ".", "target": "web", "deploy": "web" },
  { "name": "job", "dockerfile": "Dockerfile", "context": ".", "target": "job", "deploy": "job:job" }
]
```

- `target`: the Dockerfile stage to build (`docker build --target`). One Dockerfile can produce several images.
- `deploy`: `web`, or `job:<name>` for a job defined in the app definition.
- **Each image's `ENTRYPOINT` defines what it runs.** The platform never sets command or arguments.

## Runtime contract

| What | How |
|---|---|
| Port | HTTP on `8080` (`ASPNETCORE_HTTP_PORTS`), TLS terminated by the platform; `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is set, so the app sees `https` and the client IP |
| Health | `GET /health` returns 200 **as soon as the process runs, also without data or capabilities** (first deployment, empty share, paused database). Probes, the CI contract check and smoke tests depend on it. Report missing data in the app itself (for example a 503 or a "no data yet" page), never by failing to start |
| Identity | User-assigned managed identity; client ID in `AZURE_CLIENT_ID`. Use `ManagedIdentityCredential` in Azure, `DefaultAzureCredential` locally |
| Telemetry | OpenTelemetry to Application Insights via `APPLICATIONINSIGHTS_CONNECTION_STRING` |
| Signed-in user | Easy Auth header `X-MS-CLIENT-PRINCIPAL` (when the app uses `auth: entra`) |
| Component | `PLATFORM_COMPONENT` = `web` or `job`; `APP_SLUG` = `kbo` |

### Private NuGet packages (GitHub Packages)

Packages from the `co-creative` GitHub Packages feed (for example the design system `CoCreative.UI`) restore with
the workflow's own token, no personal tokens:

- `nuget.config` adds `https://nuget.pkg.github.com/co-creative/index.json` with username `co-creative` and
  password `%NUGET_AUTH_TOKEN%` (`ClearTextPassword`); the test step sets `NUGET_AUTH_TOKEN`.
- The Dockerfile restores with the BuildKit secret `github_token`:
  `RUN --mount=type=secret,id=github_token NUGET_AUTH_TOKEN="$(cat /run/secrets/github_token)" dotnet restore`.
- Each package grants this repository read access (package settings, *Manage Actions access*; an organization
  owner does this once per package and repository).
- Locally: `gh auth refresh -s read:packages`, then set `NUGET_AUTH_TOKEN` to `gh auth token` in your shell.

### Capability settings (environment variables, never secrets)

| Capability | Variables |
|---|---|
| Storage | `STORAGE__ACCOUNTNAME`, `STORAGE__BLOBENDPOINT`, `STORAGE__QUEUEENDPOINT`, `STORAGE__TABLEENDPOINT` |
| File share | mounted at the share's `mountPath` (default `/data`), read-only or read-write per component. **Read-only is not enforced yet** (managed-identity mounts are in preview): open shared files read-only in components that shouldn't write. **No file locks:** SQLite (or anything relying on byte-range locks) can't write on the share ("database is locked"); build or update such files on local disk (`/tmp`, up to 8 GiB above 1 vCPU) and copy the finished file to the share. Reading with `immutable=1` / read-only is fine |
| SQL | `SQL__SERVER`, `SQL__DATABASE`, `ConnectionStrings__Sql` (managed identity; no password) |
| Service Bus | `SERVICEBUS__NAMESPACE`, `SERVICEBUS__QUEUES` |
| SignalR service | `Azure__SignalR__ConnectionString` (managed identity), `SIGNALR__ENDPOINT` |
| AI | `AI__ENDPOINT`, `AI__DEPLOYMENT` (Azure OpenAI-compatible, managed identity) |

## Local development

How to run this app locally is described in the repository's own README. For apps created from the
`dotnet-aca-v1` template:

```
dotnet run --project src/App.AppHost      # or: aspire start
dotnet test
```

Locally there's no Easy Auth (template apps sign you in as "Local Developer" in `Development`), and capability
settings are absent unless you set them to real resources; your own Azure login is then used.
