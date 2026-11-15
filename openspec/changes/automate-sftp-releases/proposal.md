## Why

KBO Full extracts are available over SFTP, but the release job currently depends on a manual upload. Automating acquisition removes that step while preserving the existing validated, immutable release flow.

## What Changes

- Have the existing build job find and download newer Full extracts over SFTP before building and publishing them.
- Add incremental processing of consecutive KBO Update extracts, with a Full rebuild when a safe incremental chain is unavailable.
- Run the existing job daily, applying consecutive Updates between monthly Full rebuilds; retain a configurable schedule.
- Authenticate with a platform-provided Key Vault integration and verify the independently confirmed SSH host fingerprint.
- Retain manual uploads, validation, atomic publication and rollback; surface acquisition failures without replacing the current release.

## Capabilities

### New Capabilities

- `sftp-release-acquisition`: Secure, scheduled acquisition of Full extracts and handoff to the release pipeline.
- `incremental-releases`: Apply KBO Update extracts to produce equivalent immutable releases, with safe recovery via Full extracts.

### Modified Capabilities

None.

## Impact

- Affects `Net.Code.Kbo.Build`, release data/state, its tests, job configuration and `specs/azure-architecture.md`; API and UI contracts are unchanged.
- Depends on the platform's in-progress Key Vault capability, including runtime identity access and an agreed credential delivery and rotation contract. No new job or ceiling is assumed.
- SFTP authentication introduces a third-party password. It must not be committed, logged or put in ordinary app settings; the server host key must be verified.
- Daily releases, retained incremental state and Key Vault increase compute, storage transactions and potentially storage costs; measure against the EUR 10/month platform budget.
- Non-goals: resurrecting the legacy EF Core projects, changes to company data or search semantics, and platform implementation of Key Vault.
