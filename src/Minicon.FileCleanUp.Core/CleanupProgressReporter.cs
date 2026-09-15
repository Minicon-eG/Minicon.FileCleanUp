using Microsoft.Extensions.Logging;

namespace Minicon.FileCleanUp;

/// <summary>Publishes immutable snapshots independently of blocking filesystem calls.</summary>
internal sealed class CleanupProgressReporter(
    ILogger logger, TimeProvider clock, Guid runId, DateTimeOffset startedUtc, bool dryRun)
{
    private Snapshot current = new(null, null, "Starting", 0, 0, 0, 0, 0);
    private readonly long started = clock.GetTimestamp();

    internal void Update(string? rule, string? path, string phase, CleanupStatistics statistics)
    {
        Volatile.Write(ref current, new Snapshot(rule, path, phase,
            statistics.FilesScanned, statistics.CandidateFiles, statistics.FilesDeleted,
            statistics.ErrorCount, statistics.RetryCount));
    }

    internal void Report(object? _)
    {
        var snapshot = Volatile.Read(ref current);
        try
        {
            logger.LogInformation(new EventId(1050, "CleanupProgress"),
                "{EventType}: Utc={EventTimestampUtc:O}; RunId={RunId}; StartUtc={RunStartedUtc:O}; DryRun={DryRun}; ElapsedSeconds={ElapsedSeconds}; Phase={Phase}; Rule={RuleName}; Path={Path}; Scanned={FilesScanned}; Candidates={CandidateFiles}; Deleted={FilesDeleted}; Errors={ErrorCount}; Retries={RetryCount}",
                "CleanupProgress", clock.GetUtcNow(), runId, startedUtc, dryRun,
                clock.GetElapsedTime(started).TotalSeconds, snapshot.Phase, snapshot.Rule, snapshot.Path,
                snapshot.Scanned, snapshot.Candidates, snapshot.Deleted, snapshot.Errors, snapshot.Retries);
        }
        catch (Exception)
        {
            // A failing diagnostic sink must not crash the process on a timer thread.
            // Durable deletion evidence remains the responsibility of the audit journal.
        }
    }

    private sealed record Snapshot(
        string? Rule, string? Path, string Phase, long Scanned,
        long Candidates, long Deleted, long Errors, long Retries);
}
