# Static apps

Public static sites (HTML, Blazor WebAssembly) on Azure Static Web Apps **Free**, served from the global edge.

- Resource `swa-<slug>` in `rg-<slug>` (West Europe: Static Web Apps isn't offered in Sweden Central).
- Free allows **2 custom domains** per site. Hostnames and DNS are platform-owned.
- Optional managed Functions API (`.NET isolated`) and Static Web Apps built-in sign-in, configured in
  `staticwebapp.config.json` (app-owned). Route rules (for example `/api/*` for `authenticated`) answer before any
  function: an HTTP 401 on `/api/...` doesn't prove the API is deployed. The delivery checks deployed functions
  through Azure instead.
- A static site **behind Entra sign-in for the co-creative tenant** isn't a static app: it's a container app with
  `auth: entra` (Static Web Apps restricts tenant sign-in to the paid Standard plan).
- No deployment token is stored: the workflow reads it at run time through OIDC.
