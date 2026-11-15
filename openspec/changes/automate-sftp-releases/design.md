## Context

The existing daily job reads manually uploaded Full ZIPs, builds a local SQLite release, validates it and switches `current.txt` last. Published databases are opened immutable by the web app. KBO now provides daily Full and Update files over SFTP; extract numbers are sequential in the observed listing, including two extracts with the same date.

The current documents discard older NACE versions while the search index uses them, and the contentless FTS index does not preserve its source text. Therefore the published database alone is not necessarily sufficient to reconstruct an exact next release.

## Goals / Non-Goals

**Goals:** Automate acquisition, support a configurable schedule and safely produce equivalent releases from incremental updates.

**Non-Goals:** Modify a database used by readers, reintroduce the legacy EF Core import stack, or implement the platform's Key Vault capability here.

## Decisions

### Scheduled job and SFTP

Use the existing `build` job and its configurable cron schedule, proposing `0 6 1 * *` (monthly, UTC) initially. A daily schedule can be selected without a code change. Keep the existing manual release entry point independent of SFTP.

Use SFTP, not FTP/FTPS, and the `opendata` directory. Parse exact Full and Update filename patterns and compare extract numbers, not timestamps. Discover and apply all required intermediate updates; never infer a complete chain solely from a date. Verify the provider's documented sequence and Update semantics against the cookbook and real files before implementation.

The independently confirmed ED25519 SHA-256 host-key fingerprint is `SHA256:LcC2+VnBH01X/CGMd44inc/lxMJy/jH1RtwhW9PIGD8`. Reject unknown or changed keys. Retrieve credentials via the platform's approved Key Vault/managed-identity contract, which is still being established. Neither passwords nor secret-bearing connection strings belong in settings, arguments or logs.

Download to a non-candidate temporary path, check transfer completion and remote size when available, and rename only after completion. ZIP processing and existing validation remain mandatory; size alone does not establish correctness.

### Incremental source state

Keep a durable, versioned, lossless source state sufficient to reproduce the Full build, including all activity versions and source relationships. Seed it from a Full extract. Apply the provider-defined delete/insert semantics to a separate working copy in sequence, then regenerate affected documents, summaries, codes and search entries. Code changes that affect descriptions or indexing must invalidate all affected records. Choose an FTS update strategy compatible with the existing contentless index, or rebuild the index safely; do not assume contentless rows can be updated like ordinary rows.

The exact staging schema must preserve duplicate and multi-row semantics. Legacy import code is a reference for investigation, not an authority or a reusable production dependency. A Full rebuild remains the recovery path for a missing chain, expired baseline, incompatible state version or unsupported transition.

### Publish and recovery

Associate each durable state snapshot with its exact extract and release. Persist a complete new state and validated database under new names before switching `current.txt`; a crash before or after the switch must leave a matching state or cause a Full rebuild, never reuse mismatched state. Retain the current and previous release for rollback. A rollback to a release without compatible retained state triggers Full recovery before further incremental work. The next scheduled automatic run may publish a newer release again; pause the job schedule when a rollback must remain in effect.

Serialize the entire release operation across scheduled, manual and retried runs using a crash-recoverable lease or equivalent. An abandoned lease must be recoverable without allowing two active publishers. Temporary downloads and incomplete work are isolated and cleaned up safely; retain only bounded state and archives required for recovery.

### Equivalence gate and capacity

Before enabling incremental publication, compare incremental outputs against Full builds for the same targets, including data, codebook, search and insert/delete edge cases. Keep Full processing as the safe fallback. Measure state size, peak memory, ephemeral disk, duration and cost on real extracts: the current job has 8 GiB ephemeral disk and a Full database is already over 4 GiB. If staging plus output does not fit, stop and agree a storage or architecture change rather than enabling an unsafe path.

Alternatives considered: rebuilding from Full every run is simpler and remains the recovery path, but does not restore incremental processing. Updating the published SQLite file in place breaks the immutable-reader contract. A new job would require a platform component change and is unnecessary for the proposed flow.

## Risks / Trade-offs

- **Update semantics or source fidelity are misunderstood** -> verify official files and compare against Full results before enabling incremental releases.
- **The daily sequence has gaps or ages out** -> use a newer Full extract to reseed.
- **Host key rotates or credentials expire** -> fail visibly; rotate the trusted key or credential through an approved procedure.
- **Additional state exceeds disk, time or budget** -> benchmark real data before deployment and agree changes with the platform if needed.
- **Crash, overlapping jobs or rollback mismatches state** -> lease, versioned state and last-step publication; fall back to Full when association is uncertain.

## Migration Plan

1. Agree the platform secret contract, verify Update semantics, and benchmark a representative real extract and update chain.
2. Deploy with automatic acquisition and incremental publication disabled until equivalence and capacity gates pass; retain the manual Full path.
3. Configure the proposed monthly schedule and credentials securely, seed a baseline from Full, then enable automatic acquisition and incremental processing.
4. On failure, disable automation and use the manual Full path; point `current.txt` to the retained previous release if rollback is needed. Pause scheduled publication to keep a rollback in effect.
