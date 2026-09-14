namespace Minicon.FileCleanUp;

public sealed class CleanupOptions
{
    public List<string> ProtectedDirectories { get; set; } = [];
    public int MinimumRetentionDays { get; set; } = 14;
    public int DeleteDelayMilliseconds { get; set; }
    public ResilienceOptions Resilience { get; set; } = new();
    public AuditOptions Audit { get; set; } = new();
    public int MaxFilesToDeletePerRun { get; set; } = 50000;
    public bool DryRun { get; set; } = true;
    public List<CleanupRuleOptions> Rules { get; set; } = [];
}
