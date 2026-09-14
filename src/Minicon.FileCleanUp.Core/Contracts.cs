namespace Minicon.FileCleanUp;

public interface IFileCleanUpService { Task<CleanupResult> RunAsync(CancellationToken cancellationToken = default); }

public interface ICleanupAuditJournal : IDisposable
{
    void Append(AuditRecord record);
    IReadOnlyList<AuditRecord> GetPending();
    void Acknowledge(Guid operationId, string @operator, string reason);
}

public interface ICleanupAuditRecoveryService
{
    IReadOnlyList<AuditRecord> Inspect();
    void Acknowledge(Guid operationId, string @operator, string reason);
}

/// <summary>External randomness boundary for repeatable retry tests. Return a factor between 0.8 and 1.2.</summary>
public interface IRetryJitter { double NextFactor(); }
public sealed class RandomRetryJitter : IRetryJitter { public double NextFactor() => 0.8 + Random.Shared.NextDouble() * 0.4; }

