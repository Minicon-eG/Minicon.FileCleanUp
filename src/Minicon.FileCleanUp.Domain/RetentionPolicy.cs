namespace Minicon.FileCleanUp;

/// <summary>Pure UTC retention decisions, independent of filesystem access.</summary>
internal static class RetentionPolicy
{
    internal static DateTimeOffset Cutoff(DateTimeOffset runStartedUtc, int retentionDays) => runStartedUtc.AddDays(-retentionDays);
    internal static DateTimeOffset Cutoff(DateTimeOffset runStartedUtc, CleanupRuleOptions rule)
    {
        var relative = Cutoff(runStartedUtc, rule.RetentionDays);
        return rule.MaxAgeDateTime is { } fixedDate && fixedDate < relative
            ? fixedDate.ToUniversalTime()
            : relative;
    }

    internal static bool IsExpired(DateTimeOffset timestampUtc, DateTimeOffset cutoffUtc) => timestampUtc < cutoffUtc;
}
