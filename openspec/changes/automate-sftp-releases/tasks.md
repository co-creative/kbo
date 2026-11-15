## 1. Prerequisites and feasibility

- [x] 1.1 Agree and document the platform Key Vault credential delivery, identity access and rotation contract.
- [ ] 1.2 Verify KBO Update archive structure, ordering, keys, deletion and insertion semantics against the official cookbook and consecutive real extracts.
- [ ] 1.3 Prototype lossless baseline storage and measure real-extract disk, memory, duration and cost; stop for an agreed architecture change if platform limits or budget are exceeded.

## 2. Secure acquisition

- [ ] 2.1 Implement filename parsing and extract selection by number, including same-date extracts and missing chains.
- [x] 2.2 Integrate SFTP using the verified host fingerprint and platform credential contract without exposing secrets.
- [x] 2.3 Download into isolated temporary paths, check completeness, handle interruption and retain the independent manual Full path.
- [x] 2.4 Add focused tests for selection, host-key and authentication failure, incomplete transfers and manual operation.

## 3. Incremental release generation

- [ ] 3.1 Seed a versioned, lossless incremental baseline from a Full extract, preserving source relationships and all indexing inputs.
- [ ] 3.2 Apply a complete Update chain to isolated state using verified KBO insertion and deletion semantics; fall back to Full for missing or incompatible state.
- [ ] 3.3 Regenerate affected documents, summaries, codes and search entries with Full-equivalent behavior, including code and NACE changes.
- [ ] 3.4 Implement isolated versioned publication, consistent state association, crash recovery, bounded retention and a recoverable cross-run publisher lease.
- [ ] 3.5 Add fixture tests for inserts, deletions, related entities, codes, NACE versions, gaps, retry, rollback, concurrent runs and interrupted publication.

## 4. Validation and rollout

- [ ] 4.1 Compare incremental and Full outputs for the same real extract chain, including source counts, documents, code descriptions and representative search results.
- [ ] 4.2 Measure end-to-end resource use and cost against the job's limits and platform budget; do not enable incremental publication until equivalence and capacity gates pass.
- [ ] 4.3 Configure the proposed monthly schedule and enable incremental publication only after the platform secret contract and gates are ready.
- [ ] 4.4 Update `specs/azure-architecture.md` with the revised design, cadence, rollback procedure and measurements; run the repository test suite.
- [ ] 4.5 Agree the username/password JSON payload with the operator; enroll the secret and redeploy to establish the scoped grant.
- [ ] 4.6 Verify a fresh production ingestion, a new credential version on a subsequent execution, and denied secret access from the web identity.
