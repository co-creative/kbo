# Configuration and own resources

## platform.app.json (container apps)

```json
{
  "$schema": "./.github/platform/platform.app.schema.json",
  "web": { "cpu": "0.25", "memory": "0.5Gi", "minReplicas": 0, "maxReplicas": 1, "env": { "Feature__X": "on" } },
  "jobs": { "nightly": { "cpu": "0.5", "memory": "1Gi", "schedule": "0 3 * * *", "replicaTimeout": 1800, "env": {} } }
}
```

- Applied on every delivery, with the images, one revision per component.
- Memory is 2 GiB per vCPU. Values above the ceilings (tags `ceiling.*` on `rg-<slug>`) fail the delivery; the
  platform raises ceilings.
- Only components that exist may appear (`web`, job names from the platform definition). A new job needs the
  platform first.
- Settings removed from `env` are removed on the next delivery (only those this file declared before).
- `env` holds **non-secret** settings only. They're visible to platform admins.
- `minReplicas: 1` keeps a replica warm and costs money: ask first.

## infra/app.bicep (own Azure resources)

Uses the platform's registry module; names are derived from the slug, so existing resources are adopted in place:

```bicep
module resources 'br:crcocreative.azurecr.io/bicep/app-resources:1.1.0' = {
  name: 'app-resources-module'
  params: {
    slug: '<slug>'
    storage: true                               // blobs, queues, tables (+ data roles for the runtime identity)
    fileShares: [ { name: 'data', quotaGb: 5 } ] // mounts are declared by the platform
    sql: true                                   // free-tier database, runtime identity is Entra admin
    queues: [ 'work' ]                          // Service Bus Basic
    signalr: true                               // SignalR Free
  }
}
output env object = resources.outputs.env
```

- Removing a resource from the file doesn't delete it (incremental deployment). Deleting data is a deliberate,
  explicit action; the platform can lock data that can't be rebuilt.
- Access is Entra-only (no keys, no SQL logins). There are no network rules: identity is the control.
- The free SQL offer allows 10 databases per subscription; a paused serverless database takes up to a minute to
  resume, so prefer Table Storage for small, frequently read data.

## Static apps: platform.app.json

```json
{ "static": { "dotnet": "10.0.x", "test": ["dotnet test -c Release"], "build": ["dotnet publish src/Site -c Release -o publish"],
              "verify": ["test -f publish/wwwroot/index.html"], "output": "publish/wwwroot", "api": "" } }
```

`api` is the **folder** of a managed Functions project (not its `.csproj`), or empty.
