namespace Minicon.FileCleanUp;

public sealed class CleanupResult
{
    public Dictionary<string, CleanupStatistics> RuleStatistics { get; } = [];
    public DateTimeOffset StartedUtc { get; internal set; }
    public DateTimeOffset CompletedUtc { get; internal set; }
    public TimeSpan Duration { get; internal set; }
    public string? ConfigFingerprint { get; internal set; }
    public bool IsPartial { get; internal set; }
    public bool LimitReached { get; internal set; }
    public Guid RunId { get; } = Guid.NewGuid();
    public CleanupStatus Status { get; internal set; }
    public CleanupStatistics Statistics { get; } = new();
}
