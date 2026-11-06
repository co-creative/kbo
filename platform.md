# kbo on the co-creative platform

Belgian enterprise (KBO Open Data) lookup: API and web UI

- **URL:** https://kbo.co-creative.be (health check: `/health`, always anonymous)
- **Azure resources:** resource group `rg-kbo` (Sweden Central), shared Container Apps environment and registry `crcocreative.azurecr.io`
- **Infrastructure definition:** `apps/kbo/main.bicepparam` in the platform repository (co-creative/platform).
  Scale, settings, authentication and capabilities are changed there, not in this repository.

> This file and `.github/workflows/platform-deploy.yml` are owned by the platform: updates arrive as pull
> requests. You own everything else, including `platform.images.json`.

## Delivery

Every push to `main` runs `.github/workflows/platform-deploy.yml`:

1. `dotnet test`
2. builds each image in `platform.images.json`, runs the **contract check** (each web image must start on the runner
   without Azure and answer `/health` with 200; opt out per image with `"smoke": false`), then pushes it to
   `crcocreative.azurecr.io/kbo/<name>:<commit sha>`
3. points the web app (`kbo-web`) and jobs (`kbo-<job>`) at the new images
4. records the delivery on the resource group (tags `lastDeliverySha`, `lastDeliveryAt`)
5. smoke-tests `https://kbo.co-creative.be/health`

The CI identity can only push images and update this app's container apps and jobs.

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
| Port | HTTP on `8080` (`ASPNETCORE_HTTP_PORTS`), TLS terminated by the platform |
| Health | `GET /health` returns 200 **as soon as the process runs, also without data or capabilities** (first deployment, empty share, paused database). Probes, the CI contract check and smoke tests depend on it. Report missing data in the app itself (for example a 503 or a "no data yet" page), never by failing to start |
| Identity | User-assigned managed identity; client ID in `AZURE_CLIENT_ID`. Use `ManagedIdentityCredential` in Azure, `DefaultAzureCredential` locally |
| Telemetry | OpenTelemetry to Application Insights via `APPLICATIONINSIGHTS_CONNECTION_STRING` |
| Signed-in user | Easy Auth header `X-MS-CLIENT-PRINCIPAL` (when the app uses `auth: entra`) |
| Component | `PLATFORM_COMPONENT` = `web` or `job`; `APP_SLUG` = `kbo` |

### Capability settings (environment variables, never secrets)

| Capability | Variables |
|---|---|
| Storage | `STORAGE__ACCOUNTNAME`, `STORAGE__BLOBENDPOINT`, `STORAGE__QUEUEENDPOINT`, `STORAGE__TABLEENDPOINT` |
| File share | mounted at the share's `mountPath` (default `/data`), read-only or read-write per component. **Read-only is not enforced yet** (managed-identity mounts are in preview): open shared files read-only in components that shouldn't write |
| SQL | `SQL__SERVER`, `SQL__DATABASE`, `ConnectionStrings__Sql` (managed identity; no password) |
| Service Bus | `SERVICEBUS__NAMESPACE`, `SERVICEBUS__QUEUES` |
| SignalR service | `Azure__SignalR__ConnectionString` (managed identity), `SIGNALR__ENDPOINT` |
| AI | `AI__ENDPOINT`, `AI__DEPLOYMENT` (Azure OpenAI-compatible, managed identity) |

## Local development

```
dotnet run --project src/App.AppHost      # or: aspire start
dotnet test
```

Locally there's no Easy Auth: in `Development` the app signs you in as "Local Developer". Capabilities are
"not enabled" unless you set the variables above to real resources; your own Azure login is then used.
