# Implementation status — 0.1.0-preview.1

This is a working development preview, not a production-certified release. The HTML handbook is the target concept; this file records the actual delivery boundary.

## Implemented

- .NET 10 library and customer-console example; public configuration/DI entry points.
- Injectable TestableIO `IFileSystem`, .NET `TimeProvider`, `IRetryJitter`, `ILogger` and audit journal.
- Dry run, UTC age cutoff, filename filters, Path/Glob/Regex directory selectors, exclusions, recursion and empty-directory handling.
- Validation, target preflight, target de-duplication, protected roots, reparse checks, cancellation and deletion limits.
- Configurable bounded read retries with backoff/jitter and time budgets; retry of guaranteed file-sharing failures with revalidation. Uncertain deletions remain unresolved and are never blindly retried.
- Per-run and per-rule statistics, correlated structured logging and Seq sample integration.
- Local exclusive journal with framed records, SHA-256 chain checks, flush-to-disk requests, pending-path protection on restart and explicit recovery acknowledgement.
- NuGet packing, offline HTML copy for build/publish and an opt-out; real fresh-consumer package smoke test.
- 47 tests, sample build and fresh-consumer package smoke passed on Windows, Linux and macOS: [verified CI run](https://github.com/Minicon-eG/Minicon.FileCleanUp/actions/runs/34815893482). This does not replace the SMB/power-loss acceptance checks below.

## Remaining acceptance / deliberate preview limitations

- Real SMB interruption tests, Windows ACL/lock/reparse integration tests and power-loss persistence validation are still required before customer production use. In-memory tests do not prove those guarantees.
- Journal segment rotation, automated corrupt-tail repair and archive management are not implemented. A corrupt journal blocks the run; do not remove it to bypass unresolved operations. Restore/repair requires a controlled operator process. The current journal retains its record history in memory, so long-term/high-volume operation needs rotation work.
- Detailed event catalog alignment (including selector/exclusion indexes, per-attempt timings, stable event-name mapping) and remaining operational dashboards need finalization. The core emits structured per-file reasons, timestamps, rule, run and operation identifiers.
- Read-only reconciliation/export is available through inspection and serialization; the sample lists pending records and acknowledges them. It does not automatically determine the identity of a replacement file or repair a corrupt journal.
- No distributed lock, no rollback, no guarantee against hostile concurrent path replacement. A file that is reported missing during an IO race may yield a partial error rather than a successful skip.
- No NuGet.org publication or production deployment is performed by the CI workflow. The locally produced `.nupkg` is for evaluation.

## How to evaluate

1. Build and test on the target Windows environment.
2. Use a disposable directory tree with known files and an independent journal directory.
3. First run with `DryRun=true`; verify candidates and logs.
4. Test disconnect/reconnect and restart behavior before enabling scheduled deletion on real data.
