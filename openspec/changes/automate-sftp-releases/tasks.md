## 1. Prerequisites and feasibility

- [x] 1.1 Agree and document the platform Key Vault credential delivery, identity access and rotation contract.
- [x] 1.2 Verify KBO Update archive structure, ordering, keys, deletion and insertion semantics against the official cookbook and consecutive real extracts.
  - Compared the 0542 → 0543 → 0544 and 0544 → 0545 source-row multisets with corresponding Full extracts; 0524 is a valid empty Update.
- [x] 1.3 Prototype lossless baseline storage and measure real-extract disk, memory, duration and cost; stop for an agreed architecture change if platform limits or budget are exceeded.
  - A local 4-vCPU, 8-GiB Linux container processed 0544 → 0545 through validation and publication in 4m52s, with 3,317 MiB peak process memory and 4,787 MiB peak work files. SFTP and Azure Files were simulated locally; production timings and charges need monitoring.

## 2. Secure acquisition

- [x] 2.1 Implement filename parsing and extract selection by number, including same-date extracts and missing chains.
- [x] 2.2 Integrate SFTP using the verified host fingerprint and platform credential contract without exposing secrets.
- [x] 2.3 Download into isolated temporary paths, check completeness, handle interruption and retain the independent manual Full path.
- [x] 2.4 Add focused tests for selection, host-key and authentication failure, incomplete transfers and manual operation.

## 3. Incremental release generation

- [x] 3.1 Seed a versioned, lossless incremental baseline from a Full extract, preserving source relationships and all indexing inputs.
- [x] 3.2 Apply a complete Update chain to isolated state using verified KBO insertion and deletion semantics; fall back to Full for missing or incompatible state.
- [x] 3.3 Regenerate affected documents, summaries, codes and search entries with Full-equivalent behavior, including code and NACE changes.
- [x] 3.4 Implement isolated versioned publication, consistent state association, crash recovery, bounded retention and a recoverable cross-run publisher lease.
- [x] 3.5 Add fixture tests for inserts, deletions, related entities, codes, NACE versions, gaps, retry, rollback, concurrent runs and interrupted publication.

## 4. Validation and rollout

- [x] 4.1 Compare incremental and Full outputs for the same real extract chain, including source counts, documents, code descriptions and representative search results.
  - On 0544 → 0545, all 1,976,355 documents and summaries, source counts, code rows and 815,860 FTS vocabulary entries matched; 25 representative searches returned the same ordered results.
- [x] 4.2 Measure end-to-end resource use and cost against the job's limits and platform budget; do not enable incremental publication until equivalence and capacity gates pass.
  - The existing share used 9.30 GiB of its 20 GiB quota; staging a similarly sized database and state would bring that to about 14 GiB before cleanup. At Sweden Central retail rates, 31 ten-minute runs cost about $2.23 compute before the shared free grant; transaction and storage costs are additional.
- [x] 4.3 Configure the daily schedule and monthly Full rebuilds, and enable incremental publication only after the platform secret contract and gates are ready.
- [x] 4.4 Update `specs/azure-architecture.md` with the revised design, cadence, rollback procedure and measurements; run the repository test suite.
- [x] 4.5 Agree the username/password JSON payload with the operator; enroll the secret and redeploy to establish the scoped grant.
- [ ] 4.6 Verify a fresh production ingestion and the first incremental production release.
  - Production execution `kbo-build-shdhx77` published extract 0545 on 2026-11-15 (Full, built before incremental publication was deployed, so without a source snapshot).
  - Execution `kbo-build-29914920` rebuilt 0546 as a Full on 2026-11-17 and stored `.state/source-0546.zip`; the next extract should take the incremental path.
  - Dropped by decision (2026-11-17): rotation is not tested because the SFTP credential is not expected to change; no live denied-read test from the web identity. Configuration check: `id-kbo` (web) has only AcrPull; only `id-kbo-build-secrets` (job only) has Key Vault Secrets User, scoped to `kbo--build--sftp-credential`.
