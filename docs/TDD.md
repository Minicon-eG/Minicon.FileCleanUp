# TDD execution record

The user confirmed these public test boundaries before the first test:

1. `IFileCleanUpService.RunAsync` and observable filesystem/result/log behavior.
2. `AddFileCleanUp` with host-owned `IConfiguration`.
3. Audit journal persistence and recovery.
4. Actual NuGet consumption, build and publish.

The TDD skill was already installed at the start. No duplicate installation was needed.

## Observed red → green increments

Each following behavior was introduced with a failing test, observed failing, then implemented and rerun with the previous suite:

- Dry run preserves the old file and counts a candidate.
- Real deletion retains the exact cutoff boundary.
- Invalid retention prevents every deletion.
- Glob/regex selects targets and exclusions protect subtrees.
- Empty directories are predicted/removed while roots remain.
- Filename exclusions and read-only attributes preserve files.
- Invalid roots, overlaps and malformed configuration fail before deletion.
- Cancellation and deletion limits stop processing.
- Temporary listing failures recover without duplicate candidates.
- Exhausted/non-transient listing errors return an error result.
- An unfinished journal intent survives reopening and can be acknowledged.
- Intent persistence failure prevents deletion.
- Pending paths are protected while other files are processed.
- Host configuration is bound and the last provider wins.
- Deletion events carry path, rule and evaluated cutoff.
- Reparse directories are skipped.
- Changes during intent persistence prevent deletion.
- Required audit records both file and directory operations.
- Lost deletion responses are not retried as certain failures.
- Required mode opens its configured journal.
- Starts/completions are correlated, including cancellation.
- Unknown configuration returns an invalid result through the host integration.
- A missing later root prevents earlier deletion.
- The configured attempt limit is honored.
- The documented settings can be bound.
- Invalid global settings prevent deletion.
- Metadata read errors use retry handling.
- The package supplies offline HTML and build integration (package smoke test first failed for missing HTML).
- Configured deletion pauses can be cancelled.
- Guaranteed sharing violations can be retried with revalidation.
- Statistics are separate per rule.
- Recovery inspection/acknowledgement is exposed as a public service.

Later changes consolidated run lifecycle, retry classification, target preflight and correlated logging under the existing behavior suite. A real filesystem regression test also deletes 100 test-owned old files with the journal enabled, preserves a recent file and reopens the journal.

## Boundaries and limits

MockFileSystem verifies selection and effects, not the exact semantics of SMB, Windows ACLs, reparse point resolution or hardware persistence. These require Windows/network acceptance tests. FakeTimeProvider advances timers virtually; tests do not wait through production backoff intervals. The test-only fault proxies substitute external IO, not internal validators or matchers.

Run:

```text
dotnet test -c Release
python scripts/package-smoke.py
```
