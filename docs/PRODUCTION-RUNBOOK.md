# Windows / UNC production runbook

Package: `Minicon.FileCleanUp`, version `1.0.0`.

The supported deployment being prepared is a customer-owned Windows console on .NET 10, accessing UNC shares. This runbook distinguishes automated evidence from customer acceptance. Do not equate a green CI run with validation of a particular NAS, service account, antivirus policy or Seq server.

## Deployment

1. Assign exactly one cleanup host to each target tree. Configure Task Scheduler to not start a second instance. All manual/scheduled invocations must use the same local state directory so the console lock applies.
2. Provision three separate locations: executable, local state/Seq spool, and local audit directory. Keep them outside every search root. The sample protects its executable and state directories through `ProtectedDirectories`; roots may not contain or be contained in those protected paths. Existing protected-path ancestors must not be reparse points. Customer hosts must register their own output locations likewise.
3. Give the execution account read/list/delete permissions on the intended share paths and write permission on state/audit. Use UNC paths, not mapped drive letters. Do not run under an administrator account simply to bypass permission failures.
4. Register the customer's configuration provider before reading settings. Use one source for complete indexed rule arrays. The sample binds after provider registration and validates before creating state; the package also supports binding an `IConfigurationSection` for each run.
5. Enable `Audit.Mode=Required` and set its local `JournalDirectory`. Keep `DryRun=true` initially. Configure the customer's Seq URL/key through its configuration provider; never put credentials in source control.
6. Use the same Windows account, host and UNC paths as the scheduled task for acceptance. Check the fixed UTC cutoff and excluded directories against a known inventory. Review logs by `RunId` and `OperationId`.
7. Start with a small deletion limit and test-only files before the first real data run. Record the package version, configuration fingerprint, report, Seq query and expected/actual counts.

## Tested behavior and limits

- Automated tests exercise real Windows UNC access using a disposable local SMB share: online deletion, unavailable share and a successful next run after share restoration. This is not a packet-loss or mid-request server crash test.
- Real process-kill acceptance verifies durable intent recovery, multi-segment replay, protection of a replacement at the same path, and explicit operator acknowledgement.
- Deterministic external-IO fault tests cover lost deletion responses, retry deadlines, audit-write failures and mutation revalidation. A lost response is not retried as though deletion certainly failed.
- Windows storage acceptance covers real sharing locks, ACL-denied deletion and junctions. These checks passed in [CI run 34818315469](https://github.com/Minicon-eG/Minicon.FileCleanUp/actions/runs/34818315469).
- The scale regression checks 100,000 synthetic in-memory files, count accuracy and preservation in dry run. It is not a production throughput or strict-journal disk benchmark. Measure Required-mode throughput on the actual deployment storage.
- Path checks cannot make a path-based delete atomic against hostile replacement after the last check. Restrict write access and coordinate producers; age/size comparison is not a persistent file identity.
- Synchronous OS IO can outlive a cancellation request or retry budget. Budgets bound new attempts and delays, not an already blocked kernel call.

## Audit storage and recovery

The journal uses `audit.lock`, `audit.initialized`, `audit.checkpoint`, `audit.journal` and numbered `audit.*.archive` segments. Keep these together. Initialization and the checkpoint detect loss of an initialized active journal and loss/truncation of acknowledged records; the hash/sequence chain detects missing or damaged segments. This is not protection against an administrator replacing the complete history and its checkpoint.

The default segment threshold is 64 MiB. Rotation occurs before the next append after that threshold; an individual frame can add up to 16 MiB. The public journal constructor permits a different threshold. Records larger than the frame limit are rejected before changing the journal. Only unresolved intents are retained in memory. All segments are streamed on reopen, so startup time and disk consumption still grow with history. Segments are not automatically deleted or compacted.

Each append requests disk flush for its frame and checkpoint before returning. A torn checkpoint, partial frame or integrity mismatch blocks new deletion. No automatic corrupt-tail repair is provided: guessing which durable intents were lost is unsafe. A power failure can affect directory metadata/storage guarantees beyond process-level flush tests. The particular storage stack must be qualified if hardware-loss guarantees are required.

For pending operations with an intact journal:

```text
Minicon.FileCleanUp.Console.exe --audit-list
Minicon.FileCleanUp.Console.exe --audit-ack OPERATION-GUID --operator DOMAIN\user --reason "Verified disposition against the source inventory"
```

Inspection is read-only. Acknowledgement records an operator decision, not a claimed successful delete. Do not acknowledge a replacement file merely to unblock the scheduler without deciding whether it should be eligible in a subsequent run.

For corruption or a full audit disk: stop the scheduled task, retain the complete affected directory and logs, establish enough local capacity and investigate. Do not delete the journal, marker, checkpoint or individual archives to resume cleanup. A backup must be consistent and complete; an older backup can omit later outstanding operations. Reconcile the interval since that backup before any restoration is allowed to resume deletion. The library deliberately fails closed rather than performing automatic repair.

Back up the complete journal directory while no writer is running. External copies may be archived; current local segments must remain available for replay. Monitor capacity and startup duration. Long-term retention/compaction requires a separately reviewed procedure.

## Monitoring and exit codes

Alert on exit codes 1/2, repeated code 3, unknown outcomes, `AuditUnavailable`, `DeletionOutcomeUnknown`, `StorageScopeUnavailable`, and a missing `CleanupCompleted` after the expected runtime. Code 4 means the configured file limit was reached; code 5 means cooperative cancellation. Code 0 includes successful dry runs and zero candidates.

Useful Seq filters:

```text
Application = 'Minicon.FileCleanUp.Console' and EventType = 'CleanupCompleted'
EventType = 'DeletionOutcomeUnknown' or EventType = 'AuditUnavailable'
EventType = 'FileSystemRetryScheduled'
EventType = 'FileDeleted' and RuleName = 'Exports'
```

The supplied host buffers Seq events on local disk. End-to-end Seq ingestion, outage buffering and delivery after recovery still need validation against the customer's Seq installation. ILogger alone is not a durable delivery acknowledgement; Required audit is separate.

## Customer acceptance record

Before scheduling actual customer data, record:

- Windows and .NET versions, share server/NAS version and UNC test paths.
- Execution identity, share + filesystem ACLs, and producer coordination.
- Dry-run inventory comparison and a small controlled real-delete run.
- Temporary share outage/recovery and process restart, with expected partial status and preserved replacements.
- Seq outage/recovery, persistent spool capacity and received correlated events.
- Required-mode runtime, audit size growth, backup/recovery responsibility and capacity alerts.

No credentials, customer share or Seq instance were available during implementation. No NuGet.org publication or customer production deployment has been performed.
