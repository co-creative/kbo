# Reusable packages: the co-creative NuGet feed

The organization has a private NuGet feed on GitHub Packages: `https://nuget.pkg.github.com/co-creative/index.json`.
It holds libraries shared between apps, for example the design system (`CoCreative.Tokens`, `CoCreative.UI`, from
`co-creative/co-creative-ui`). Apps **look there before building something reusable**, and **publish there** when
code is worth sharing.

## Look for a package first

Before writing a UI component, an Azure helper or other code another app may already have:

```sh
gh auth refresh -s read:packages                        # once
gh api "orgs/co-creative/packages?package_type=nuget" --jq '.[].name'
dotnet package search CoCreative --source co-creative   # needs the nuget.config below and NUGET_AUTH_TOKEN
```

Or browse <https://github.com/orgs/co-creative/packages>. Each package links to its source repository (README,
samples). Prefer an existing package over a copy; if it lacks something, propose the change in its repository
instead of forking the code.

## Consume a package

1. `nuget.config` at the repository root (no secret in it):

   ```xml
   <configuration>
     <packageSources>
       <clear />
       <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
       <add key="co-creative" value="https://nuget.pkg.github.com/co-creative/index.json" />
     </packageSources>
     <packageSourceMapping>
       <packageSource key="co-creative"><package pattern="CoCreative.*" /></packageSource>
       <packageSource key="nuget.org"><package pattern="*" /></packageSource>
     </packageSourceMapping>
     <packageSourceCredentials>
       <co-creative>
         <add key="Username" value="co-creative" />
         <add key="ClearTextPassword" value="%NUGET_AUTH_TOKEN%" />
       </co-creative>
     </packageSourceCredentials>
   </configuration>
   ```

2. Dockerfile: restore with the BuildKit secret the platform workflow provides, then build without restoring:

   ```dockerfile
   COPY Directory.Build.props nuget.config ./
   COPY src/ src/
   RUN --mount=type=secret,id=github_token,required=true \
       NUGET_AUTH_TOKEN="$(cat /run/secrets/github_token)" dotnet restore src/App.Web
   RUN dotnet publish src/App.Web -c Release --no-restore -o /out/web
   ```

   The platform's `platform-deploy.yml` already sets `NUGET_AUTH_TOKEN` for tests and passes the secret to image
   builds (the workflow's own token, read-only). Never pass a token as a build argument.

3. Locally: `export NUGET_AUTH_TOKEN=$(gh auth token)` (PowerShell: `$env:NUGET_AUTH_TOKEN = gh auth token`), after
   `gh auth refresh -s read:packages`. A local `docker build` needs `--secret id=github_token,env=NUGET_AUTH_TOKEN`.

4. Pin exact versions (`Version="2.0.0-preview.1"`); upgrade deliberately.

**Access:** packages are private and grant read access **per repository** (package settings, *Manage Actions
access*; GitHub UI only, no API). A restore that fails with 401/403 in CI means this repository isn't granted yet:
ask the user (an organization owner) to add it. Don't work around it with a personal token.

## Publish a package

Publish when code is genuinely reused by two or more apps (or clearly will be), not speculatively.

- **Name:** `CoCreative.<Area>` (for example `CoCreative.Azure.Tables`), so consumers' source mapping finds it on the
  co-creative feed. Set `PackageId`, `Version` (SemVer; `-preview.N` while unstable), `Description`,
  `RepositoryUrl` (links the package to its repository) and a README.
- **Workflow:** the repository's own workflow (not the platform-owned `platform-deploy.yml`), triggered by a `v*`
  tag, with `permissions: packages: write` and the workflow token:

  ```yaml
  on:
    push:
      tags: ['v*']
  permissions:
    contents: read
    packages: write
  jobs:
    publish:
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@v7
        - uses: actions/setup-dotnet@v6
          with: { global-json-file: global.json }
        - run: dotnet test -c Release
        - run: dotnet pack src/<Project> -c Release -o artifacts
        - run: dotnet nuget push "artifacts/*.nupkg" --source https://nuget.pkg.github.com/co-creative/index.json --api-key ${{ github.token }} --skip-duplicate
  ```

  Check that the tag matches the packed version before pushing. `co-creative/co-creative-ui` (`ci.yml`) is a
  working example.
- **No secrets** in the package (configuration, endpoints of a specific app, keys). Libraries take settings from the
  consuming app.
- **After the first publish:** ask the user to grant the consuming repositories read access to the new package
  (*Manage Actions access*), and mention the package in the source repository's README.
- **Breaking changes** bump the major version; consumers upgrade at their own pace.
