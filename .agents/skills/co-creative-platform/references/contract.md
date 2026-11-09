# Runtime contract (container apps)

`platform.md` in this repository is authoritative for this app; this is the summary.

| What | Rule |
|---|---|
| Port (web) | HTTP on 8080 (`ASPNETCORE_HTTP_PORTS`). TLS is terminated by the platform; `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is set, so the app sees `https` and the client IP. |
| Health (web) | `GET /health` returns 200 **as soon as the process runs**, also without data, capabilities or a reachable database. It's anonymous (excluded from sign-in). Report missing data in the app, never by failing to start. The CI contract check starts each web image on the runner without Azure and requires `/health` 200 within 60 s. |
| Jobs | No port, no health endpoint. A job runs once and exits: exit code 0 means succeeded, non-zero marks the execution failed. It's stopped after `replicaTimeout` seconds (`platform.app.json`). |
| Images | One Dockerfile target per component (`web`, job names), listed in `platform.images.json`. Each image's `ENTRYPOINT` defines what runs: the platform never sets command or args. Chiseled .NET runtime images (no shell, non-root) are the default. |
| Identity | User-assigned managed identity; client ID in `AZURE_CLIENT_ID`. `ManagedIdentityCredential(AZURE_CLIENT_ID)` in Azure, `DefaultAzureCredential` locally. |
| Telemetry | OpenTelemetry to Application Insights via `APPLICATIONINSIGHTS_CONNECTION_STRING`. |
| Signed-in user | Easy Auth header `X-MS-CLIENT-PRINCIPAL` (apps with `auth: entra`); group claims if enabled. |
| Platform variables | `APP_SLUG`, `PLATFORM_COMPONENT` (`web`/`job`), `AZURE_CLIENT_ID`, `APPLICATIONINSIGHTS_CONNECTION_STRING`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, `AI__*`: reserved, can't be set in `platform.app.json`. |
| Capability settings | From `infra/app.bicep` outputs: `STORAGE__*`, `SQL__*`, `ConnectionStrings__Sql` (managed identity, no password), `SERVICEBUS__*`, `SIGNALR__*`. |

## Pitfalls seen on this platform

- **Scale to zero:** Data Protection keys and caches are in memory; with more than one replica or after a cold start,
  antiforgery tokens and protected cookies don't survive. Keep `maxReplicas: 1` or persist keys.
- **File shares (SMB) have no byte-range locks:** SQLite can't write on them ("database is locked"). Build such files
  on local disk (`/tmp`, up to 8 GiB above 1 vCPU) and copy the result to the share; reading with `immutable=1` is fine.
- **Read-only mounts aren't enforced** yet: open shared files read-only in components that shouldn't write.
