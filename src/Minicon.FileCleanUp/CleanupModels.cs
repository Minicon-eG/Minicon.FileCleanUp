namespace Minicon.FileCleanUp;

public enum AuditMode { LoggingOnly, Required }
public sealed class AuditOptions { public AuditMode Mode { get; set; } public string? JournalDirectory { get; set; } }
public sealed class ResilienceOptions
{
    public int MaxAttempts { get; set; } = 6;
    public int InitialDelaySeconds { get; set; } = 2;
    public int MaxDelaySeconds { get; set; } = 30;
    public int MaxRetryElapsedSecondsPerOperation { get; set; } = 120;
    public int MaxRetryElapsedSecondsPerRun { get; set; } = 300;
}
public sealed class CleanupOptions
{
    public int MinimumRetentionDays { get; set; } = 14;
    public int DeleteDelayMilliseconds { get; set; }
    public ResilienceOptions Resilience { get; set; } = new();
    public AuditOptions Audit { get; set; } = new();
    public int MaxFilesToDeletePerRun { get; set; } = 50000;
    public bool DryRun { get; set; } = true;
    public List<CleanupRuleOptions> Rules { get; set; } = [];
}
public sealed class CleanupRuleOptions
{
    public string Timestamp { get; set; } = "LastWriteTimeUtc";
    public List<string> IncludePatterns { get; set; } = ["*"];
    public List<string> ExcludePatterns { get; set; } = [];
    public bool RemoveEmptyDirectories { get; set; }
    public bool Recursive { get; set; }
    public List<DirectorySelector> ExcludeDirectories { get; set; } = [];
    public string Name { get; set; } = "";
    public int RetentionDays { get; set; }
    public List<DirectorySelector> Directories { get; set; } = [];
}
public enum SelectorKind { Path, Glob, Regex }
public sealed class DirectorySelector { public SelectorKind Kind { get; set; } public string? Pattern { get; set; } public string Root { get; set; } = ""; }
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
public enum CleanupStatus { Succeeded, InvalidConfiguration, CompletedWithErrors, Failed, Limited, Cancelled }
public sealed class CleanupResult
{
    public Dictionary<string, CleanupStatistics> RuleStatistics { get; } = [];
    public DateTimeOffset StartedUtc { get; internal set; }
    public DateTimeOffset CompletedUtc { get; internal set; }
    public TimeSpan Duration { get; internal set; }
    public string? ConfigFingerprint { get; internal set; }
    public bool IsPartial { get; internal set; }
    public bool LimitReached { get; internal set; }
    public Guid RunId { get; } = Guid.NewGuid(); public CleanupStatus Status { get; internal set; }
    public CleanupStatistics Statistics { get; } = new();
}
public interface IFileCleanUpService { Task<CleanupResult> RunAsync(CancellationToken cancellationToken = default); }
