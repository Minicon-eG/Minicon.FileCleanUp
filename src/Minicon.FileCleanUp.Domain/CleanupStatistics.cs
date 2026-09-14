namespace Minicon.FileCleanUp;

public sealed class CleanupStatistics
{
    public long FilesScanned { get; internal set; }
    public long DirectoriesScanned { get; internal set; }
    public long CandidateBytes { get; internal set; }
    public long DeletedFileBytes { get; internal set; }
    public long DeleteAttempts { get; internal set; }
    public long ErrorCount { get; internal set; }
    public Dictionary<string, long> SkippedByReason { get; } = [];
    public long DirectoriesDeleted { get; internal set; }
    public long DirectoriesWouldBeDeleted { get; internal set; }
    public long UnknownDeletionOutcomes { get; internal set; }
    public long RetryCount { get; internal set; }
    public long CandidateFiles { get; internal set; }
    public long FilesDeleted { get; internal set; }
}
