# Delivery

How changes reach the default branch (push, `pr-auto` or `pr-review`) is the repository's choice in
`.co-creative.yml`; the delivery rules (`co-creative-delivery`) describe each flow. Whatever the flow, run the tests
locally before every push.

`.github/workflows/platform-deploy.yml` (platform-owned):

**Pull requests:** the test job only. Nothing is built, pushed or deployed.

**Push to the default branch (container apps):**
1. Tests.
2. Deploy `infra/app.bicep` to `rg-<slug>` (if present) and read its `env` output.
3. Validate `platform.app.json`: components exist, no reserved names, within the ceilings.
4. Build each image in `platform.images.json`; contract check (`/health` 200 without Azure); push to
   `crcocreative.azurecr.io/<slug>/<name>:<commit sha>`.
5. One update per component: image + resources + scale/schedule + env.
6. Record the delivery (`lastDeliverySha`, `lastDeliveryAt` tags), verify, smoke-test `https://<host>/health`.

**Push (static apps):** test, build, verify → OIDC login → deployment token read at run time → deploy → API check
(functions listed through Azure when `api` is set) → tags → smoke test on the primary hostname.

## Reading a failed delivery

| Symptom | Likely cause |
|---|---|
| `AADSTS700213` at Azure login | Federated credential doesn't match the branch/repo: ask the platform. |
| Contract check fails | The web image needs data or Azure to start; make `/health` independent. |
| "declare the component in apps/<slug> first" | `platform.app.json` names a job the platform doesn't know. |
| Above the ceiling | Lower the value or ask the platform to raise the ceiling. |
| `RequestDisallowedByPolicy` in "App resources" | A resource type, SKU or setting the platform policies don't allow. |
| Smoke test times out on a first deployment | Certificate still issuing; re-run the job later. |

Never work around a failure by weakening the contract, adding secrets, or editing platform-owned files.
