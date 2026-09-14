# Changelog

## 1.0.0 — 2026-09-14

First stable release of `Minicon.FileCleanUp` for customer-owned .NET 10 console applications, focused on Windows and UNC network shares.

- Host-owned IConfiguration binding and injectable filesystem, time, logging and audit boundaries.
- Safe default dry run; fixed UTC retention cutoff; filename filters; Path/Glob/Regex directory selectors and exclusions.
- Optional recursive traversal and deletion of empty subdirectories while preserving cleanup roots.
- Configuration preflight, protected host directories, reparse-point checks, mutation revalidation, cancellation and file limits.
- Bounded retry attempts/time budgets, structured retry details, per-run/per-rule statistics and Seq-compatible ILogger events.
- Optional Required audit with durable intents/results, segmented hash-linked journal, integrity checkpoint, exclusive writer protection, pending-path recovery and explicit acknowledgement.
- Standalone offline HTML copied into consuming applications during build/publish.
- Customer-console example with local state lock, reports, Seq buffering and scheduler exit codes.

Validation: 61 automated tests, package build/publish/opt-out acceptance, real process-kill recovery, and Windows SMB outage/restart, sharing-lock, ACL-denial and junction checks. A 100,000-file synthetic dry run verifies count accuracy; it is not a NAS throughput benchmark.

Operating limits: no automatic corrupt-journal repair/compaction, distributed lock, rollback, hardware power-loss guarantee or atomic file-identity protection against hostile concurrent replacement. Journal segments remain locally available for replay. Customer deployment, actual share permissions and Seq delivery acceptance are outside the NuGet package.
