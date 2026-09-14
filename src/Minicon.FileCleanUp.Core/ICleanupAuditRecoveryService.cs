namespace Minicon.FileCleanUp;

public interface ICleanupAuditRecoveryService
{
    IReadOnlyList<AuditRecord> Inspect();
    void Acknowledge(Guid operationId, string @operator, string reason);
}
