# Changelog

## 1.3.0 — 2026-09-15

Add configurable periodic CleanupProgress messages at Information level.
- Default interval: 60 seconds; ProgressIntervalSeconds supports 1–86400 seconds, or 0 to disable.
- Report run/time, current rule/path/phase, scanned/candidate/deleted files, errors and retries.
- Use immutable snapshots and an independent TimeProvider timer so messages continue while filesystem calls wait.
- Stop and drain the timer before completion; progress is diagnostic, not proof of forward movement.
- Update offline documentation.

Validation: 102 tests; package, host and Windows storage acceptance gate publication.

## 1.2.2 — 2026-09-15

Detailed logging for Console and Seq:
- Log skipped files and directories at Trace.
- Include UTC timestamps, rule names, source selectors, exclusions, filters and retention settings in readable messages.
- Explain TooRecent decisions with evaluated timestamps and effective cutoff.
- Preserve structured Seq properties and align message templates.
- Include updated offline documentation.

Validation: 97 automated tests; publication is gated on package, host and Windows storage acceptance.

## 1.2.1 — 2026-09-15

- Add host-owned asynchronous legacy JSON rule-provider integration without a database dependency.
- Map existing source directories, wildcard paths, filename filters, exclusions, recursion and timestamp flags.
- Support MaxAgeDateTime as a fixed cutoff: only strictly older timestamps qualify. Combine with retention limits using the earliest cutoff. Legacy dates without an offset are UTC.
- Validate all rules before deletion. Allow shared wildcard search roots and nonrecursive parent/recursive child rules; reject conflicting resolved targets.
- Prune wildcard searches outside matching prefixes and document legacy exclusion semantics in the bundled HTML.

Validation: 95 automated tests and package-consumer checks. Release workflow additionally gates publication on Windows host and storage acceptance.
Compatibility: .NET 10 remains required. Console integration needs the new provider callback; database JSON does not need migration. Do not mix modern Rules with provider rules. Legacy directory exclusion stars can span separators; filename exclusions are filename patterns only.

## 1.1.0 — 2026-09-15

- Accept legacy global configuration names `DelayBetweenDelete` (milliseconds) and `MinimumAgeInDays`; reject conflicting old/new values and unknown keys.
- Add flags-based `CheckDates`: every selected creation/write/access UTC timestamp must strictly precede the retention cutoff. Default remains last write; empty/unknown selections are rejected.
- Revalidate selected timestamps before deletion and retries. Record the observed timestamps and selected flags in audit and logs.
- Separate Domain, Core, Infrastructure and entry-point assemblies, bundled in one NuGet package. Preserve existing public namespaces with type forwarding.
- Improve source readability and enforce formatting in CI. Update the bundled offline handbook and integration examples.

Validation: 82 tests plus package-consumer, process recovery and Windows storage acceptance checks.

Compatibility: .NET 10 remains required. This release does not include the full legacy `FileCleanUpSetting` provider adapter, `MaxAgeDateTime`, or legacy absolute-path exclusion conversion. Cleanup rules with target directories remain required. Reflection limited to the original assembly may need updating. Do not downgrade an active audit journal after a version upgrade without validating compatibility.

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
