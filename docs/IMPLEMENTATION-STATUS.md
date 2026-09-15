# Implementation status — 1.2.0

This file records the scope of the 1.2.0 package release. Customer-specific Windows, share, identity and Seq acceptance is a deployment responsibility, separate from the package release. Documented extensions are not part of the 1.2.0 contract.

## Implemented

- Legacy JSON rule-provider adapter, wildcard source/exclusion mapping and fixed MaxAgeDateTime cutoff combined with minimum age and retention.
- 95 local automated tests and fresh package-consumer checks passed for 1.2.0. Windows acceptance is enforced by the publishing workflow.

- Legacy global aliases and CheckDates flags with AND semantics, timestamp revalidation and detailed audit values.
- Four source assemblies shipped in one NuGet package, preserving existing public namespaces through forwarding.

- .NET 10 library and customer-console example; public configuration/DI entry points.
- Injectable TestableIO `IFileSystem`, .NET `TimeProvider`, `IRetryJitter`, `ILogger` and audit journal.
- Dry run, UTC age cutoff, filename filters, Path/Glob/Regex directory selectors, exclusions, recursion and empty-directory handling.
- Validation, target preflight, target de-duplication, protected roots, reparse checks, cancellation and deletion limits.
- Configurable bounded read retries with backoff/jitter and time budgets; retry of guaranteed file-sharing failures with revalidation. Uncertain deletions remain unresolved and are never blindly retried.
- Per-run and per-rule statistics, correlated structured logging and Seq sample integration.
- Local exclusive journal with framed records, SHA-256 chain checks, flush-to-disk requests, pending-path protection on restart and explicit recovery acknowledgement.
- NuGet packing, offline HTML copy for build/publish and an opt-out; real fresh-consumer package smoke test.
- Prior 1.1.0 baseline: 82 tests, fresh package installation and process-kill acceptance passed on Windows/Linux/macOS. Windows SMB/restart, lock, ACL and junction acceptance also passed: [verified CI run](https://github.com/Minicon-eG/Minicon.FileCleanUp/actions/runs/34949197533). CodeQL completed successfully on the same code revision.

## Production hardening delivered

- Automatic 64 MiB journal segmentation; replay retains only pending operations in memory.
- Persisted checkpoint detects complete-frame truncation; initialized journal loss and missing segments fail closed.
- Serialized concurrent journal calls and an append-size bound prevent self-created corrupt histories.
- File size/timestamp and directory/reparse revalidation after audit persistence.
- Strict global retry deadline, structured retry attempt/delay/timing and selector/exclusion indexes.
- Protected executable/state paths in the sample, checked before creating logs, including reparse ancestors.
- 100,000-file synthetic scale regression and actual killed-process/restart/recovery acceptance.
- Windows storage acceptance with a disposable SMB share, restart after unavailability, file lock, ACL denial and junction protection.

## Remaining customer acceptance / deliberate limitations

- The customer confirmed Windows with network shares. Actual Windows/NAS versions, execution account, disposable customer share and Seq test instance were not provided. Validate those with [the production runbook](PRODUCTION-RUNBOOK.md).
- A local SMB share test does not prove packet-loss, mid-request remote server crash or hardware/power-loss guarantees. Seq delivery after an actual server outage is not yet verified.
- Journal archives are retained locally and replayed on startup. Disk use/startup time grow; there is no automatic deletion, compaction or corrupt-tail repair. Corruption fails closed. Never remove archives or restore a stale journal to bypass pending operations.
- Path-based deletion is not a file-identity transaction. No distributed lock, rollback or protection against hostile concurrent path replacement is claimed.
- Detailed additional target-concept events such as JournalId/JournalSequence in Seq and automatic missing-file reconciliation are not implemented. The HTML distinguishes delivered behavior from these extensions.
- Publication status is available on NuGet.org. Customer production deployment is not certified by package publication.
- Legacy JSON integration requires a host-owned callback. Database access and provider retries remain in the host.

## How to evaluate

1. Build and test on the target Windows environment.
2. Use a disposable directory tree with known files and an independent journal directory.
3. First run with `DryRun=true`; verify candidates and logs.
4. Test disconnect/reconnect and restart behavior before enabling scheduled deletion on real data.
